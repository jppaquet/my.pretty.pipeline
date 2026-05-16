using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;

namespace Notify.Functions.Auth;

// Backs the user allowlist with a Cosmos point-read by sub. Cache layer:
//   - Approved=true is cached for 60 seconds so a chatty client doesn't pay
//     a Cosmos RU on every request.
//   - Approved=false is NOT cached — once an admin flips the flag in Data
//     Explorer the user's next request must see it without waiting for a
//     TTL.
//
// Self-registration on miss: a 404 from ReadItem triggers a CreateItem with
// `approved=false`. Concurrent first-sign-in races collapse via the 409
// catch — whichever request wins the create, the other just observes the
// pending row.
public sealed class CosmosAllowlistRepository : IAllowlistRepository
{
    private static readonly TimeSpan ApprovedCacheTtl = TimeSpan.FromSeconds(60);

    private readonly Container _container;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _clock;

    public CosmosAllowlistRepository(Container container, IMemoryCache cache, TimeProvider? clock = null)
    {
        _container = container;
        _cache = cache;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<bool> IsApprovedAsync(string sub, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sub))
            return false;

        if (_cache.TryGetValue<bool>(CacheKey(sub), out var cached) && cached)
            return true;

        try
        {
            var response = await _container.ReadItemAsync<AllowedUserDocument>(
                sub, new PartitionKey(sub), cancellationToken: ct);
            if (response.Resource.Approved)
            {
                _cache.Set(CacheKey(sub), true, ApprovedCacheTtl);
                return true;
            }
            return false;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            var doc = new AllowedUserDocument
            {
                Id = sub,
                Sub = sub,
                Approved = false,
                FirstSeenAt = _clock.GetUtcNow(),
            };
            try
            {
                await _container.CreateItemAsync(doc, new PartitionKey(sub), cancellationToken: ct);
            }
            catch (CosmosException dup) when (dup.StatusCode == HttpStatusCode.Conflict)
            {
                // Concurrent first-sign-in race — another worker won the
                // insert; the row exists and is `approved=false`.
            }
            return false;
        }
    }

    private static string CacheKey(string sub) => "allowlist:" + sub;
}
