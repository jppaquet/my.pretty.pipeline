using Microsoft.Azure.NotificationHubs;

namespace Notify.Functions.Devices;

// Seam between the handler and Microsoft.Azure.NotificationHubs.NotificationHubClient
// so unit tests can record installations without standing up a real hub.
public interface INotificationHub
{
    Task UpsertInstallationAsync(Installation installation, CancellationToken ct = default);
}

public sealed class NotificationHubAdapter : INotificationHub
{
    private readonly NotificationHubClient _client;

    public NotificationHubAdapter(NotificationHubClient client) => _client = client;

    public Task UpsertInstallationAsync(Installation installation, CancellationToken ct = default)
        => _client.CreateOrUpdateInstallationAsync(installation, ct);
}
