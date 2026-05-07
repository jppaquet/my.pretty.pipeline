using System.Net;
using Microsoft.Azure.Cosmos;
using Notify.Shared.Cosmos;

namespace Notify.Functions.Archive;

// Seam between ArchiveHandler and Cosmos so unit tests can verify the doc
// shape without standing up the emulator.
public interface IArchiveSink
{
    Task<ArchiveOutcome> StoreAsync(NotificationDocument document, CancellationToken ct = default);
}

public enum ArchiveOutcome
{
    Created,
    DuplicateIgnored,
}

public sealed class CosmosArchiveSink : IArchiveSink
{
    private readonly Container _notifications;

    public CosmosArchiveSink(Container notifications) => _notifications = notifications;

    public async Task<ArchiveOutcome> StoreAsync(NotificationDocument document, CancellationToken ct = default)
    {
        try
        {
            await _notifications.CreateItemAsync(
                document,
                new PartitionKey(document.Source),
                cancellationToken: ct);
            return ArchiveOutcome.Created;
        }
        catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.Conflict)
        {
            // Same dedup key seen within the 90-day TTL — by design, swallow.
            return ArchiveOutcome.DuplicateIgnored;
        }
    }
}
