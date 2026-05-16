using Microsoft.Azure.NotificationHubs;
using Notify.Functions.Devices;
using Notify.Shared.Cosmos;

namespace Notify.Functions.Devices.Tests;

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

internal sealed class InMemoryDeviceStore : IDeviceStore
{
    public List<DeviceDocument> Upserted { get; } = new();

    public Task UpsertAsync(DeviceDocument document, CancellationToken ct = default)
    {
        Upserted.Add(document);
        return Task.CompletedTask;
    }
}
