using Microsoft.Azure.Cosmos;
using Notify.Shared.Cosmos;

namespace Notify.Functions.Inbox;

// Seam between InboxHandler and Cosmos so unit tests can drive the handler
// without standing up the emulator (mirror of IArchiveSink). The userId is
// always supplied by the handler (extracted from the validated JWT in the
// HTTP shim) and is the security boundary for the query — never trust the
// query string here.
public interface IInboxQuery
{
    Task<InboxPage> QueryAsync(string userId, string? source, int limit, string? continuationToken, CancellationToken ct = default);
}

public sealed record InboxPage(IReadOnlyList<NotificationDocument> Items, string? ContinuationToken);

public sealed class CosmosInboxQuery : IInboxQuery
{
    private readonly Container _notifications;

    public CosmosInboxQuery(Container notifications) => _notifications = notifications;

    public async Task<InboxPage> QueryAsync(string userId, string? source, int limit, string? continuationToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        // Cross-partition by default — the inbox spans every `source` partition
        // the user is subscribed to. When the caller supplies a source filter,
        // we also constrain the partition for a cheaper point-partition query.
        //
        // `isHidden` filter: rows written before the field existed have no
        // `c.isHidden` property, so coalesce missing → false to keep showing
        // pre-migration rows in the inbox.
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.userId = @userId "
                + "AND (NOT IS_DEFINED(c.isHidden) OR c.isHidden = false) "
                + "ORDER BY c.timestamp DESC")
            .WithParameter("@userId", userId);
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
