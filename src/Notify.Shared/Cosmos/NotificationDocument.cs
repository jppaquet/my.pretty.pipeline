using System.Text.Json;
using System.Text.Json.Serialization;

namespace Notify.Shared.Cosmos;

// Cosmos document shape for `notify.notifications` (partition key /source).
// `id` is either the envelope id (UUID v7) or, when the producer set a
// deduplicationKey, sha256(source + ":" + dedupKey) so re-sends collide on
// CreateItem and Notify.Archive can swallow the 409.
//
// `ttl` is omitted for "keep forever" rows (set to -1) and inherits the
// container's 90d default otherwise.
public sealed class NotificationDocument
{
    [JsonPropertyName("id")]               public required string Id { get; init; }
    [JsonPropertyName("source")]           public required string Source { get; init; }
    [JsonPropertyName("type")]             public required string Type { get; init; }
    [JsonPropertyName("title")]            public required string Title { get; init; }
    [JsonPropertyName("body")]             public required string Body { get; init; }
    [JsonPropertyName("priority")]         public Priority Priority { get; init; }
    [JsonPropertyName("tags")]             public IReadOnlyList<string>? Tags { get; init; }
    [JsonPropertyName("deeplink")]         public string? Deeplink { get; init; }
    [JsonPropertyName("metadata")]         public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
    [JsonPropertyName("deduplicationKey")] public string? DeduplicationKey { get; init; }
    [JsonPropertyName("timestamp")]        public required DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("readAt")]           public DateTimeOffset? ReadAt { get; init; }
}
