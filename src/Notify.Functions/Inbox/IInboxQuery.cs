using Microsoft.Azure.Cosmos;
using Notify.Shared.Cosmos;

namespace Notify.Functions.Inbox;

// Seam between InboxHandler and Cosmos so unit tests can drive the handler
// without standing up the emulator (mirror of IArchiveSink).
public interface IInboxQuery
{
    Task<InboxPage> QueryAsync(string? source, int limit, string? continuationToken, CancellationToken ct = default);
}

public sealed record InboxPage(IReadOnlyList<NotificationDocument> Items, string? ContinuationToken);

public sealed class CosmosInboxQuery : IInboxQuery
{
    private readonly Container _notifications;

    public CosmosInboxQuery(Container notifications) => _notifications = notifications;

    public async Task<InboxPage> QueryAsync(string? source, int limit, string? continuationToken, CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.timestamp DESC");
        var options = new QueryRequestOptions { MaxItemCount = limit };
        if (!string.IsNullOrEmpty(source))
            options.PartitionKey = new PartitionKey(source);

        var iterator = _notifications.GetItemQueryIterator<NotificationDocument>(
            query, continuationToken, options);

        // One page per request — Cosmos paging is opaque-token-driven and the
        // client surfaces the next token as `continuationToken` for the caller.
        if (!iterator.HasMoreResults)
            return new InboxPage(Array.Empty<NotificationDocument>(), null);

        var page = await iterator.ReadNextAsync(ct);
        return new InboxPage(page.ToList(), page.ContinuationToken);
    }
}
