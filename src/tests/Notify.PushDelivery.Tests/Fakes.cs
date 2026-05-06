using Notify.PushDelivery.Push;

namespace Notify.PushDelivery.Tests;

internal sealed record SentNotification(ApnsPayload Payload, string TagExpression);

internal sealed class RecordingSender : INotificationSender
{
    public List<SentNotification> Sent { get; } = new();
    public string? TrackingIdToReturn { get; init; } = "track-123";

    public Task<string?> SendApnsAsync(ApnsPayload payload, string tagExpression, CancellationToken ct = default)
    {
        Sent.Add(new SentNotification(payload, tagExpression));
        return Task.FromResult(TrackingIdToReturn);
    }
}

internal sealed class ThrowingSender : INotificationSender
{
    private readonly Exception _ex;
    public ThrowingSender(Exception ex) => _ex = ex;
    public Task<string?> SendApnsAsync(ApnsPayload payload, string tagExpression, CancellationToken ct = default)
        => Task.FromException<string?>(_ex);
}
