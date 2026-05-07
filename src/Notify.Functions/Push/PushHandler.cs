using Notify.Shared.CloudEvents;

namespace Notify.Functions.Push;

// Pure delivery logic; the Function class is a thin EG-trigger shim around it.
// NH calls go through INotificationSender so unit tests don't need a real hub.
public sealed class PushHandler
{
    private readonly INotificationSender _sender;

    public PushHandler(INotificationSender sender) => _sender = sender;

    public Task<string?> HandleAsync(CloudEventEnvelope envelope, CancellationToken ct = default)
    {
        var data = envelope.Data;
        var payload = ApnsPayload.From(data);
        var tagExpression = TagExpression.For(data.Source, data.Tags);
        return _sender.SendApnsAsync(payload, tagExpression, ct);
    }
}
