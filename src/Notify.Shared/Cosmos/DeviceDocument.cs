using System.Text.Json.Serialization;

namespace Notify.Shared.Cosmos;

// Cosmos document shape for `notify.devices` (partition key /deviceId).
// Mirror of the Notification Hub installation, plus the bind to the
// authenticated user. Written by Notify.RegisterDevice on every registration;
// read by Notify.Archive to find subscribed users for per-user fan-out.
//
// `UserId` is the validated Sign-in-with-Apple `sub` of the device's owner.
// `Tags` are server-derived from the user's identity — the client cannot
// influence what tags it subscribes to (closes the H3 finding from the
// security audit).
public sealed record DeviceDocument
{
    [JsonPropertyName("id")]         public required string Id { get; init; }
    [JsonPropertyName("deviceId")]   public required string DeviceId { get; init; }
    [JsonPropertyName("userId")]     public required string UserId { get; init; }
    [JsonPropertyName("apnsToken")]  public required string ApnsToken { get; init; }
    [JsonPropertyName("tags")]       public required IReadOnlyList<string> Tags { get; init; }
    [JsonPropertyName("updatedAt")]  public required DateTimeOffset UpdatedAt { get; init; }
}
