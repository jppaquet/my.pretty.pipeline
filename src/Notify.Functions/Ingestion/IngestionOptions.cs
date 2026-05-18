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

    // 128 KB ceiling — fits a structured batch of ~100 events, each ~1 KB (matches MaxBatchSize).
    // A single CloudEvent is well under 8 KB (title 120 chars + body 2 KB + 4 KB metadata + envelope).
    public const int MaxRequestBodyBytes = 128 * 1024;

    // CloudEvents batch cap: at most this many events per request.
    public const int MaxBatchSize = 100;
}
