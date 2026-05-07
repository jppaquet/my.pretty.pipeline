namespace Notify.Functions.Ingestion;

// Strongly-typed bindings to the app-settings the IngestionApi reads at startup.
// Configured by `infra/modules/functions.bicep`; `local.settings.json` mirrors
// it for local dev (gitignored). Key Vault references resolve transparently —
// e.g. `API_KEY_PEPPER_BASE64=@Microsoft.KeyVault(SecretUri=...)`.
public sealed record IngestionOptions
{
    public required string CosmosAccountEndpoint { get; init; }
    public string CosmosDatabase { get; init; } = "notify";
    public string CosmosProjectsContainer { get; init; } = "projects";
    public required string EventGridTopicEndpoint { get; init; }
    public required string ApiKeyPepperBase64 { get; init; }

    public const int MaxRequestBodyBytes = 8 * 1024;  // 8 KB ceiling, well above the validator's 2 KB body + metadata
}
