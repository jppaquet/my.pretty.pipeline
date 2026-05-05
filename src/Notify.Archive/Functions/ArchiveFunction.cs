using Azure.Messaging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Notify.Archive.Archiving;
using Notify.Shared.CloudEvents;
using Notify.Shared.Json;

namespace Notify.Archive.Functions;

// Event Grid trigger. The function name `archive` must match the
// `resourceId('Microsoft.Web/sites/functions', functionAppName, 'archive')`
// reference in `infra/modules/eventgrid.bicep` — renaming this function
// without updating the bicep breaks the subscription.
public sealed class ArchiveFunction
{
    private readonly ArchiveHandler _handler;
    private readonly ILogger<ArchiveFunction> _logger;

    public ArchiveFunction(ArchiveHandler handler, ILogger<ArchiveFunction> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function("archive")]
    public async Task Run([EventGridTrigger] CloudEvent ce)
    {
        var data = ce.Data?.ToObjectFromJson<NotifyCreatedV1>(NotifyJson.Options)
            ?? throw new InvalidOperationException("CloudEvent has no data payload");

        var envelope = new CloudEventEnvelope
        {
            Id = ce.Id,
            Source = ce.Source,
            Type = ce.Type,
            Time = ce.Time ?? DateTimeOffset.UtcNow,
            Data = data,
        };

        var outcome = await _handler.HandleAsync(envelope);

        if (outcome == ArchiveOutcome.DuplicateIgnored)
        {
            _logger.LogInformation(
                "Archive duplicate ignored: source={Source} dedup={Dedup}",
                envelope.Data.Source, envelope.Data.DeduplicationKey);
        }
    }
}
