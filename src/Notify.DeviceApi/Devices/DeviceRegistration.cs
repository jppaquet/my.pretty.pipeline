using System.Text.Json.Serialization;

namespace Notify.DeviceApi.Devices;

// Registration request from the iOS app. The platform field is fixed to
// "apns" for v1 — Android can land later as a sibling enum value.
public sealed record DeviceRegistration
{
    [JsonPropertyName("deviceToken")]
    public required string DeviceToken { get; init; }

    [JsonPropertyName("platform")]
    public required string Platform { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; init; }
}
