using Microsoft.Azure.NotificationHubs;

namespace Notify.PushDelivery.Push;

// Seam between PushHandler and Microsoft.Azure.NotificationHubs.NotificationHubClient
// so unit tests can record sends without standing up a real hub. We surface
// just the tracking id (string?) rather than the SDK's NotificationOutcome —
// NotificationOutcome.TrackingId is internally settable so it can't be
// constructed by tests, and we don't currently consume any other field.
public interface INotificationSender
{
    Task<string?> SendApnsAsync(ApnsPayload payload, string tagExpression, CancellationToken ct = default);
}

public sealed class NotificationHubSender : INotificationSender
{
    private readonly NotificationHubClient _client;

    public NotificationHubSender(NotificationHubClient client) => _client = client;

    public async Task<string?> SendApnsAsync(ApnsPayload payload, string tagExpression, CancellationToken ct = default)
    {
        var notification = new AppleNotification(payload.Json, new Dictionary<string, string>
        {
            ["apns-priority"] = payload.ApnsPriority.ToString(),
            ["apns-push-type"] = "alert",
        });
        var outcome = await _client.SendNotificationAsync(notification, tagExpression, ct);
        return outcome.TrackingId;
    }
}
