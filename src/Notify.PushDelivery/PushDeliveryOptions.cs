namespace Notify.PushDelivery;

// App settings PushDelivery reads at startup. Configured by
// infra/modules/functions.bicep. Keys mirror the DeviceApi options.
public sealed record PushDeliveryOptions
{
    public required string NotificationHubConnectionString { get; init; }
    public required string NotificationHubName { get; init; }
}
