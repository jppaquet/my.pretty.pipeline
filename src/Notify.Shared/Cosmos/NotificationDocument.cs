using System.Text.Json;
using System.Text.Json.Serialization;
using Notify.Shared.CloudEvents;

namespace Notify.Shared.Cosmos;

// Cosmos document shape for `notify.notifications` (partition key /source).
// Written by Notify.Archive on each `notify.created.v1` event; read by
// Notify.InboxApi when a device pulls history.
//
// `Id` is either `DedupKeyHasher.Hash(source, deduplicationKey)` (when the
// producer set a dedup key) or the CloudEvent envelope id (otherwise). Same
// (source, dedupKey) within the 90-day TTL collapses onto one document.
public sealed record NotificationDocument
{
    [JsonPropertyName("id")]               public required string Id { get; init; }
    [JsonPropertyName("source")]           public required string Source { get; init; }
    [JsonPropertyName("title")]            public required string Title { get; init; }
    [JsonPropertyName("body")]             public required string Body { get; init; }
    [JsonPropertyName("type")]             public string Type { get; init; } = "info";
    [JsonPropertyName("priority")]         public Priority Priority { get; init; } = Priority.Normal;
    [JsonPropertyName("tags")]             public IReadOnlyList<string>? Tags { get; init; }
    [JsonPropertyName("deeplink")]         public string? Deeplink { get; init; }
    [JsonPropertyName("metadata")]         public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
    [JsonPropertyName("deduplicationKey")] public string? DeduplicationKey { get; init; }
    [JsonPropertyName("timestamp")]        public required DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("envelopeId")]       public required string EnvelopeId { get; init; }

    public static NotificationDocument From(CloudEventEnvelope envelope, string id) => new()
    {
        Id = id,
        Source = envelope.Data.Source,
        Title = envelope.Data.Title,
        Body = envelope.Data.Body,
        Type = envelope.Data.Type,
        Priority = envelope.Data.Priority,
        Tags = envelope.Data.Tags,
        Deeplink = envelope.Data.Deeplink,
        Metadata = envelope.Data.Metadata,
        DeduplicationKey = envelope.Data.DeduplicationKey,
        Timestamp = envelope.Data.Timestamp ?? envelope.Time,
        EnvelopeId = envelope.Id,
    };
}
