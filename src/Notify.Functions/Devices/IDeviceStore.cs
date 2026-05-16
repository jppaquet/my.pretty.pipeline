using Microsoft.Azure.Cosmos;
using Notify.Shared.Cosmos;

namespace Notify.Functions.Devices;

// Cosmos seam for the `devices` container — RegisterHandler upserts a
// `DeviceDocument` on every registration so Archive (via IUserDirectory)
// can discover the set of subscribed users for per-user fan-out.
public interface IDeviceStore
{
    Task UpsertAsync(DeviceDocument document, CancellationToken ct = default);
}

public sealed class CosmosDeviceStore : IDeviceStore
{
    private readonly Container _devices;

    public CosmosDeviceStore(Container devices) => _devices = devices;

    public Task UpsertAsync(DeviceDocument document, CancellationToken ct = default)
        => _devices.UpsertItemAsync(document, new PartitionKey(document.DeviceId), cancellationToken: ct);
}
