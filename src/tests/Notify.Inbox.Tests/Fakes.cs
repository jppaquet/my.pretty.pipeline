using Notify.Functions.Inbox;
using Notify.Shared.Cosmos;

namespace Notify.Functions.Inbox.Tests;

internal sealed class InMemoryInboxQuery : IInboxQuery
{
    public List<NotificationDocument> Stored { get; } = new();
    public string? LastSource { get; private set; }
    public int LastLimit { get; private set; }
    public string? LastContinuationToken { get; private set; }
    public string? NextContinuationToken { get; set; }

    public Task<InboxPage> QueryAsync(string? source, int limit, string? continuationToken, CancellationToken ct = default)
    {
        LastSource = source;
        LastLimit = limit;
        LastContinuationToken = continuationToken;

        var filtered = string.IsNullOrEmpty(source)
            ? Stored
            : Stored.Where(d => d.Source == source);

        var page = filtered
            .OrderByDescending(d => d.Timestamp)
            .Take(limit)
            .ToList();

        return Task.FromResult(new InboxPage(page, NextContinuationToken));
    }
}
