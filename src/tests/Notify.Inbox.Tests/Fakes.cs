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

internal sealed class InMemoryInboxMutator : IInboxMutator
{
    public sealed record Call(string Source, string Id, string Field);
    public List<Call> Calls { get; } = new();
    public HashSet<(string Source, string Id)> Existing { get; } = new();

    public Task<InboxMutateOutcome> MarkReadAsync(string source, string id, CancellationToken ct = default)
        => Patch(source, id, "isRead");

    public Task<InboxMutateOutcome> MarkHiddenAsync(string source, string id, CancellationToken ct = default)
        => Patch(source, id, "isHidden");

    private Task<InboxMutateOutcome> Patch(string source, string id, string field)
    {
        Calls.Add(new Call(source, id, field));
        // Existing empty = treat all as present (default-update behavior for
        // tests that don't care about the not-found branch).
        if (Existing.Count > 0 && !Existing.Contains((source, id)))
            return Task.FromResult(InboxMutateOutcome.NotFound);
        return Task.FromResult(InboxMutateOutcome.Updated);
    }
}
