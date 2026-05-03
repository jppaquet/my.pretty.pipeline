using System.Text.Json;
using System.Text.Json.Serialization;

namespace Notify.Shared.CloudEvents;

// Canonical message contract producers send to POST /v1/notifications.
// Mirrors docs/SCHEMA.md. Server-fills Id, Timestamp, and overrides Source from
// the authenticated project (the producer's submitted Source is ignored).
public sealed record NotifyCreatedV1
{
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("title")]  public required string Title { get; init; }
    [JsonPropertyName("body")]   public required string Body { get; init; }

    [JsonPropertyName("type")]     public string Type { get; init; } = "info";
    [JsonPropertyName("priority")] public Priority Priority { get; init; } = Priority.Normal;

    [JsonPropertyName("tags")]             public IReadOnlyList<string>? Tags { get; init; }
    [JsonPropertyName("deeplink")]         public string? Deeplink { get; init; }
    [JsonPropertyName("metadata")]         public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
    [JsonPropertyName("deduplicationKey")] public string? DeduplicationKey { get; init; }
    [JsonPropertyName("timestamp")]        public DateTimeOffset? Timestamp { get; init; }
    [JsonPropertyName("id")]               public string? Id { get; init; }
}
