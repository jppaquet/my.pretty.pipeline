namespace Notify.Functions.Devices;

// Strongly-typed bindings for the app settings DeviceApi reads at startup.
// Configured by infra/modules/functions.bicep; local.settings.json mirrors
// it for local dev. NotificationHubConnectionString is a Key Vault reference
// at deploy time (full SAS — DeviceApi needs Manage permissions to
// CreateOrUpdate Installations).
public sealed record DevicesOptions
{
    public required string NotificationHubConnectionString { get; init; }
    public required string NotificationHubName { get; init; }

    public const int MaxRequestBodyBytes = 4 * 1024;
}
