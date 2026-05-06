using Azure.Messaging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Notify.PushDelivery.Push;
using Notify.Shared.CloudEvents;
using Notify.Shared.Json;

namespace Notify.PushDelivery.Functions;

// Event Grid trigger. The function name `push` must match the
// `resourceId('Microsoft.Web/sites/functions', functionAppName, 'push')`
// reference in `infra/modules/eventgrid.bicep` — renaming this function
// without updating the bicep breaks the subscription.
public sealed class PushFunction
{
    private readonly PushHandler _handler;
    private readonly ILogger<PushFunction> _logger;

    public PushFunction(PushHandler handler, ILogger<PushFunction> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function("push")]
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

        var trackingId = await _handler.HandleAsync(envelope);

        _logger.LogInformation(
            "Pushed: id={Id} source={Source} priority={Priority} tracking={Tracking}",
            envelope.Data.Id, envelope.Data.Source, envelope.Data.Priority, trackingId);
    }
}
