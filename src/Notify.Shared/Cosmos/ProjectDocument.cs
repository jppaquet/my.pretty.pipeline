using System.Text.Json.Serialization;

namespace Notify.Shared.Cosmos;

// Cosmos document shape for `notify.projects` (partition key /projectId).
// One per producing project; minted by docs/PROJECT-ONBOARDING.md until the
// admin endpoint lands in Phase 4.
public sealed class ProjectDocument
{
    [JsonPropertyName("id")]          public string Id { get; init; } = "";
    [JsonPropertyName("projectId")]   public string ProjectId { get; init; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("salt")]        public string SaltBase64 { get; init; } = "";
    [JsonPropertyName("keyHash")]     public string KeyHashBase64 { get; init; } = "";
    [JsonPropertyName("active")]      public bool Active { get; init; }

    [JsonIgnore] public byte[] Salt => Convert.FromBase64String(SaltBase64);
    [JsonIgnore] public byte[] KeyHash => Convert.FromBase64String(KeyHashBase64);
}
