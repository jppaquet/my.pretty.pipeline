namespace Notify.Shared.CloudEvents;

// CloudEvents 1.0 JSON envelope wrapping a NotifyCreatedV1 payload.
// What gets published to Event Grid; what subscribers (Notify.Archive,
// Notify.PushDelivery) receive.
//
// `source` follows the URN form `urn:notify:<project>` so per-project filtering
// is uniform across subscriptions.
public sealed record CloudEventEnvelope
{
    public const string CurrentType = "notify.created.v1";

    public string SpecVersion { get; init; } = "1.0";
    public required string Id { get; init; }
    public required string Source { get; init; }
    public required string Type { get; init; }
    public required DateTimeOffset Time { get; init; }
    public string DataContentType { get; init; } = "application/json";
    public required NotifyCreatedV1 Data { get; init; }

    public static CloudEventEnvelope From(NotifyCreatedV1 data, Guid id, DateTimeOffset time) => new()
    {
        Id = id.ToString("D"),
        Source = $"urn:notify:{data.Source}",
        Type = CurrentType,
        Time = time,
        Data = data,
    };
}
