using System.Text.Json;
using System.Text.Json.Serialization;
using Notify.Shared.CloudEvents;

namespace Notify.Shared.Cosmos;

// Cosmos document shape for `notify.notifications` (partition key /source).
// Written by Notify.Archive — one document per (envelope, subscribed user)
// fan-out — read by Notify.InboxApi when the user pulls history.
//
// `UserId` is the validated Sign-in-with-Apple `sub` of the recipient; the
// Inbox query filters on it server-side so a token holder only sees their
// own notifications. `Id` carries `userId` in its suffix so the same
// envelope archives to N distinct documents (one per subscribed user) within
// the same `/source` partition.
//
// Without a deduplicationKey: `Id = "{envelopeId}:{userId}"`.
// With one:                   `Id = "{DedupKeyHasher.Hash(source, dedupKey)}:{userId}"`.
// → re-sending the same (source, dedupKey) within the 90-day TTL collapses
//   per-user onto one document (the cross-user uniqueness comes from the
//   userId suffix).
public sealed record NotificationDocument
{
    [JsonPropertyName("id")]               public required string Id { get; init; }
    [JsonPropertyName("source")]           public required string Source { get; init; }
    [JsonPropertyName("userId")]           public required string UserId { get; init; }
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

    // Per-recipient mutation state set after Archive writes the row. Default
    // false on create; the Inbox mutation endpoints (POST /v1/inbox/{id}/read,
    // DELETE /v1/inbox/{id}) flip these per (id, userId). Inbox queries filter
    // IsHidden rows out so a swipe-to-delete removes the row from the iOS list
    // without losing the audit document (the 90-day TTL still applies).
    [JsonPropertyName("isRead")]           public bool IsRead { get; init; }
    [JsonPropertyName("isHidden")]         public bool IsHidden { get; init; }

    public static NotificationDocument From(CloudEventEnvelope envelope, string baseId, string userId) => new()
    {
        Id = $"{baseId}:{userId}",
        Source = envelope.Data.Source,
        UserId = userId,
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
