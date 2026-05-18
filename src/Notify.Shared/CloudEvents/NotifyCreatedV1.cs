using System.Text.Json;
using System.Text.Json.Serialization;

namespace Notify.Shared.CloudEvents;

// Notification payload carried inside a CloudEvents 1.0 envelope's `data`.
// Mirrors docs/SCHEMA.md. Source is server-locked from the authenticated
// project: the CloudEvents `source` attribute is the canonical project id on
// the wire, and the handler sets this field from `project.ProjectId` post-auth
// before validation. Any value the producer puts here is overwritten.
public sealed record NotifyCreatedV1
{
    // Source is the CloudEvents `source` attribute on the wire, not part of
    // `data`. The handler sets this field from project.ProjectId post-auth so
    // every downstream consumer reads a non-empty string. Default-empty
    // (rather than `required`) so deserialization succeeds when producers
    // omit it from data, which they should — the CE attribute is canonical.
    [JsonPropertyName("source")] public string Source { get; init; } = string.Empty;
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
