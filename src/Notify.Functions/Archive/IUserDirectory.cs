using Microsoft.Azure.Cosmos;

namespace Notify.Functions.Archive;

// Backs Archive's per-user fan-out. Returns the set of distinct user ids
// across every registered device.
//
// This is the "broadcast to every signed-in user" model: every notification
// reaches every authenticated user's inbox. The architecture supports more
// granular subscription routing (per-source allow-lists, tag policies) but
// PR-C scopes to broadcast — adding subscription policy would also need a
// producer-side ACL model, which is out of scope for the identity layer.
public interface IUserDirectory
{
    Task<IReadOnlyCollection<string>> ListActiveUserIdsAsync(CancellationToken ct = default);
}

public sealed class CosmosUserDirectory : IUserDirectory
{
    private readonly Container _devices;

    public CosmosUserDirectory(Container devices) => _devices = devices;

    public async Task<IReadOnlyCollection<string>> ListActiveUserIdsAsync(CancellationToken ct = default)
    {
        // Cross-partition by design; the devices container is bounded by the
        // total number of (user, device) pairs across the deployment. For a
        // solo deploy that's O(1) devices; cost stays trivial.
        var query = new QueryDefinition("SELECT DISTINCT VALUE c.userId FROM c WHERE IS_DEFINED(c.userId)");
        var iterator = _devices.GetItemQueryIterator<string>(query);
        var users = new HashSet<string>(StringComparer.Ordinal);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            foreach (var userId in page)
            {
                if (!string.IsNullOrWhiteSpace(userId))
                    users.Add(userId);
            }
        }
        return users;
    }
}
