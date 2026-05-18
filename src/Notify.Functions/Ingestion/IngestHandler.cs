using Notify.Shared.CloudEvents;
using Notify.Shared.Hashing;
using Notify.Shared.Validation;

namespace Notify.Functions.Ingestion;

// Pure ingestion logic; the Function class is a thin HTTP shim around it.
// Accepts an already-parsed list of CloudEvents (single = list of 1).
// Project lookup is behind IProjectLookup so unit tests don't need Cosmos;
// publishing is behind IEventPublisher for the same reason.
public sealed class IngestHandler
{
    private readonly IProjectLookup _projects;
    private readonly IEventPublisher _publisher;
    private readonly ApiKeyHasher _hasher;
    private readonly TimeProvider _clock;

    public IngestHandler(IProjectLookup projects, IEventPublisher publisher, ApiKeyHasher hasher, TimeProvider? clock = null)
    {
        _projects = projects;
        _publisher = publisher;
        _hasher = hasher;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<IngestResult> HandleAsync(
        string? apiKey,
        IReadOnlyList<IngestInput> events,
        bool isBatch,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new IngestResult.Unauthorized();

        if (events.Count == 0)
            return new IngestResult.BadRequest(new[] { new ValidationFailure("events", "must contain at least one event") });

        if (events.Count > IngestionOptions.MaxBatchSize)
            return new IngestResult.BadRequest(new[]
            {
                new ValidationFailure("events", $"max {IngestionOptions.MaxBatchSize} events per batch, was {events.Count}"),
            });

        // One project per request: the request carries one x-api-key. All
        // events in a batch must share the same `ce.source` so we can look up
        // and verify the project once.
        var firstSource = events[0].Source;
        for (var i = 1; i < events.Count; i++)
        {
            if (!string.Equals(events[i].Source, firstSource, StringComparison.Ordinal))
            {
                return new IngestResult.BadRequest(new[]
                {
                    new ValidationFailure($"events[{i}].source", $"all events in a batch must share source; expected '{firstSource}', was '{events[i].Source}'"),
                });
            }
        }

        var project = await _projects.FindAsync(firstSource, ct);
        if (project is null || !project.Active || !_hasher.Verify(apiKey, project.Salt, project.KeyHash))
            return new IngestResult.Unauthorized();

        // Server-lock source, mint id/time, validate, build envelope. Atomic:
        // any per-event validation failure aborts the whole request before
        // any publish happens.
        var failures = new List<ValidationFailure>();
        var ids = new string[events.Count];
        var envelopes = new CloudEventEnvelope[events.Count];

        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            var newId = Guid.CreateVersion7();
            var time = e.Time ?? _clock.GetUtcNow();
            var canonical = e.Data with
            {
                Id = newId.ToString(),
                Timestamp = time,
                Source = project.ProjectId,
            };

            var validation = NotifyCreatedV1Validator.Validate(canonical);
            if (!validation.IsValid)
            {
                var prefix = isBatch ? $"events[{i}]." : string.Empty;
                foreach (var f in validation.Failures)
                    failures.Add(new ValidationFailure(prefix + f.Field, f.Message));
                continue;
            }

            ids[i] = canonical.Id!;
            envelopes[i] = CloudEventEnvelope.From(canonical, newId, time);
        }

        if (failures.Count > 0)
            return new IngestResult.BadRequest(failures);

        if (isBatch)
            await _publisher.PublishBatchAsync(envelopes, ct);
        else
            await _publisher.PublishAsync(envelopes[0], ct);

        return isBatch
            ? new IngestResult.AcceptedBatch(ids)
            : new IngestResult.Accepted(ids[0]);
    }
}
