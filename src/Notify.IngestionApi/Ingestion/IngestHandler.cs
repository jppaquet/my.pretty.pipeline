using System.Text.Json;
using Notify.Shared.CloudEvents;
using Notify.Shared.Hashing;
using Notify.Shared.Json;
using Notify.Shared.Validation;

namespace Notify.IngestionApi.Ingestion;

// Pure ingestion logic; the Function class is a thin HTTP shim around it.
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

    public async Task<IngestResult> HandleAsync(string? apiKey, Stream body, long? contentLength, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new IngestResult.Unauthorized();

        if (contentLength is > IngestionApiOptions.MaxRequestBodyBytes)
            return new IngestResult.PayloadTooLarge(IngestionApiOptions.MaxRequestBodyBytes);

        NotifyCreatedV1? input;
        try
        {
            input = await JsonSerializer.DeserializeAsync<NotifyCreatedV1>(body, NotifyJson.Options, ct);
        }
        catch (JsonException ex)
        {
            return new IngestResult.BadRequest(new[] { new ValidationFailure("body", ex.Message) });
        }

        if (input is null)
            return new IngestResult.BadRequest(new[] { new ValidationFailure("body", "missing") });

        var validation = NotifyCreatedV1Validator.Validate(input);
        if (!validation.IsValid)
            return new IngestResult.BadRequest(validation.Failures);

        var project = await _projects.FindAsync(input.Source, ct);
        if (project is null || !project.Active || !_hasher.Verify(apiKey, project.Salt, project.KeyHash))
            return new IngestResult.Unauthorized();

        var id = Guid.CreateVersion7();
        var time = input.Timestamp ?? _clock.GetUtcNow();
        var canonical = input with
        {
            Id = id.ToString(),
            Timestamp = time,
            Source = project.ProjectId,  // server-locks source to the authenticated project
        };

        var envelope = CloudEventEnvelope.From(canonical, id, time);
        await _publisher.PublishAsync(envelope, ct);

        return new IngestResult.Accepted(canonical.Id!);
    }
}
