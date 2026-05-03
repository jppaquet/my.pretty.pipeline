using System.Net;
using Microsoft.Azure.Cosmos;
using Notify.Shared.Cosmos;

namespace Notify.Archive.Archive;

// Seam over Cosmos so unit tests don't need an emulator.
// Returns true on Create, false on 409 Conflict (idempotent dedup).
public interface INotificationStore
{
    Task<bool> TryCreateAsync(NotificationDocument document, CancellationToken ct = default);
}

public sealed class CosmosNotificationStore : INotificationStore
{
    private readonly Container _notifications;

    public CosmosNotificationStore(Container notifications) => _notifications = notifications;

    public async Task<bool> TryCreateAsync(NotificationDocument document, CancellationToken ct = default)
    {
        try
        {
            await _notifications.CreateItemAsync(document, new PartitionKey(document.Source), cancellationToken: ct);
            return true;
        }
        catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.Conflict)
        {
            // Producer re-sent within the dedup window — same id → 409, treat as success.
            return false;
        }
    }
}
