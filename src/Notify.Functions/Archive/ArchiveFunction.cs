using Azure.Messaging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Notify.Functions.Archive;
using Notify.Shared.CloudEvents;
using Notify.Shared.Json;

namespace Notify.Functions.Archive;

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

        var result = await _handler.HandleAsync(envelope);

        _logger.LogInformation(
            "Archive fan-out: source={Source} subscribed={Users} created={Created} duplicates={Duplicates}",
            envelope.Data.Source, result.SubscribedUsers, result.Created, result.Duplicates);

        if (result.SubscribedUsers == 0)
        {
            // Notification was published but no users have registered yet — the
            // inbox is empty for everyone. Log so it's debuggable when a fork
            // forgets to sign in on the iOS app before sending notifications.
            _logger.LogWarning(
                "Archive dropped notification: zero registered users (source={Source} envelope={EnvelopeId})",
                envelope.Data.Source, envelope.Id);
        }
    }
}
