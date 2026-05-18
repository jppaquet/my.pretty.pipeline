using System.Net;
using Microsoft.Azure.Cosmos;
using Notify.Functions.Auth;

namespace Notify.Functions.Admin;

// Backs every /admin/allowlist/* endpoint. Reads + mutates the same
// `allowedUsers` Cosmos container the iOS-side allowlist gate consults; no
// inter-process coordination needed because Cosmos is the system of record.
//
// Mutations are idempotent: approving an already-approved row updates
// approvedAt to "now" but causes no visible behavior change. Revoking a
// row that was already pending is a no-op.
public sealed class AllowlistAdminHandler
{
    private readonly Container _container;
    private readonly TimeProvider _clock;

    public AllowlistAdminHandler(Container container, TimeProvider? clock = null)
    {
        _container = container;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<AllowedUserDocument>> ListAsync(CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT c.id, c.sub, c.approved, c.firstSeenAt, c.approvedAt FROM c ORDER BY c.firstSeenAt DESC");
        var iterator = _container.GetItemQueryIterator<AllowedUserDocument>(query);
        var items = new List<AllowedUserDocument>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            items.AddRange(page);
        }
        return items;
    }

    public Task<AllowedUserDocument?> ApproveAsync(string sub, CancellationToken ct = default)
        => SetApprovedAsync(sub, true, ct);

    public Task<AllowedUserDocument?> RevokeAsync(string sub, CancellationToken ct = default)
        => SetApprovedAsync(sub, false, ct);

    private async Task<AllowedUserDocument?> SetApprovedAsync(string sub, bool approved, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sub))
            return null;

        AllowedUserDocument current;
        try
        {
            var resp = await _container.ReadItemAsync<AllowedUserDocument>(sub, new PartitionKey(sub), cancellationToken: ct);
            current = resp.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Admin trying to act on a sub that hasn't signed in yet — no
            // row to flip. Surface as null so the function returns 404.
            return null;
        }

        var now = _clock.GetUtcNow();
        var updated = current with
        {
            Approved = approved,
            // Stamp on approve; clear on revoke so a subsequent re-approve
            // gets a fresh timestamp rather than a stale one.
            ApprovedAt = approved ? now : null,
        };
        await _container.ReplaceItemAsync(updated, sub, new PartitionKey(sub), cancellationToken: ct);
        return updated;
    }
}
