using Microsoft.Azure.Cosmos;
using Notify.Functions.Auth;

namespace Notify.Functions.Admin;

// Cross-partition query over `allowedUsers` for the admin app's "list
// testers" view. Bounded by the number of distinct subs the deployment has
// ever seen, which stays small in practice (one row per sign-in attempt).
//
// Pure handler so a unit test can swap the Container for a fake without
// pulling in Cosmos. Wire-up in Program.cs picks the real container from
// AuthOptions.
public sealed class AllowlistAdminHandler
{
    private readonly Container _container;

    public AllowlistAdminHandler(Container container) => _container = container;

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
}
