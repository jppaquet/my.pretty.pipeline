using Notify.Functions.Inbox;
using Notify.Shared.Cosmos;

namespace Notify.Functions.Inbox.Tests;

internal sealed class InMemoryInboxQuery : IInboxQuery
{
    public List<NotificationDocument> Stored { get; } = new();
    public string? LastUserId { get; private set; }
    public string? LastSource { get; private set; }
    public int LastLimit { get; private set; }
    public string? LastContinuationToken { get; private set; }
    public string? NextContinuationToken { get; set; }

    public Task<InboxPage> QueryAsync(string userId, string? source, int limit, string? continuationToken, CancellationToken ct = default)
    {
        LastUserId = userId;
        LastSource = source;
        LastLimit = limit;
        LastContinuationToken = continuationToken;

        IEnumerable<NotificationDocument> filtered = Stored.Where(d => d.UserId == userId);
        if (!string.IsNullOrEmpty(source))
            filtered = filtered.Where(d => d.Source == source);

        var page = filtered
            .OrderByDescending(d => d.Timestamp)
            .Take(limit)
            .ToList();

        return Task.FromResult(new InboxPage(page, NextContinuationToken));
    }
}
