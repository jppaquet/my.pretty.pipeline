namespace Notify.Functions.Archive;

// App-settings the Archive function reads at startup. Configured by
// `infra/modules/functions.bicep`; `local.settings.json` mirrors it for dev.
public sealed record ArchiveOptions
{
    public required string CosmosAccountEndpoint { get; init; }
    public string CosmosDatabase { get; init; } = "notify";
    public string CosmosNotificationsContainer { get; init; } = "notifications";
    public string CosmosDevicesContainer { get; init; } = "devices";
}
