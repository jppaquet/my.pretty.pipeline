using Azure.Messaging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Notify.Archive.Archive;
using Notify.Shared.CloudEvents;
using Notify.Shared.Json;

namespace Notify.Archive.Functions;

public sealed class ArchiveFunction
{
    private readonly ArchiveHandler _handler;
    private readonly ILogger<ArchiveFunction> _logger;

    public ArchiveFunction(ArchiveHandler handler, ILogger<ArchiveFunction> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function("Archive")]
    public async Task Run(
        [EventGridTrigger] CloudEvent cloudEvent,
        FunctionContext context)
    {
        // Only archive notify.created.v1 — ignore other event families that may
        // share the topic in the future.
        if (cloudEvent.Type != CloudEventEnvelope.CurrentType)
        {
            _logger.LogInformation("Ignoring event type {Type}", cloudEvent.Type);
            return;
        }

        var data = cloudEvent.Data?.ToObjectFromJson<NotifyCreatedV1>(NotifyJson.Options);
        if (data is null)
        {
            _logger.LogWarning("CloudEvent {Id} has empty data; skipping", cloudEvent.Id);
            return;
        }

        var envelope = new CloudEventEnvelope
        {
            Id = cloudEvent.Id,
            Source = cloudEvent.Source,
            Type = cloudEvent.Type,
            Time = cloudEvent.Time ?? DateTimeOffset.UtcNow,
            Data = data,
        };

        var result = await _handler.HandleAsync(envelope, context.CancellationToken);

        switch (result)
        {
            case ArchiveResult.Created c:
                _logger.LogInformation("Archived {Id} (source={Source})", c.Id, data.Source);
                break;
            case ArchiveResult.AlreadyExists e:
                _logger.LogInformation("Dedup hit for {Id} (source={Source})", e.Id, data.Source);
                break;
        }
    }
}
