using Microsoft.Azure.NotificationHubs;
using Notify.DeviceApi;

namespace Notify.DeviceApi.Tests;

internal sealed class RecordingHub : INotificationHub
{
    public List<Installation> Upserted { get; } = new();

    public Task UpsertInstallationAsync(Installation installation, CancellationToken ct = default)
    {
        Upserted.Add(installation);
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingHub : INotificationHub
{
    private readonly Exception _ex;
    public ThrowingHub(Exception ex) => _ex = ex;
    public Task UpsertInstallationAsync(Installation installation, CancellationToken ct = default)
        => Task.FromException(_ex);
}
