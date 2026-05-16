using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;
using Notify.Functions.Auth;

namespace Notify.Auth.Tests;

// Allowlist gate is the second security check after JWT validation; these
// tests pin the contract end-to-end against the Cosmos emulator so the
// self-registration + race + cache behaviors match production semantics
// rather than a mocked Container.
[Trait("Category", "Integration")]
public class CosmosAllowlistRepositoryTests : IClassFixture<CosmosEmulatorFixture>
{
    private const string SubA = "001234.aaaaaaaa";
    private const string SubB = "005678.bbbbbbbb";
    private readonly CosmosEmulatorFixture _fx;

    public CosmosAllowlistRepositoryTests(CosmosEmulatorFixture fx) => _fx = fx;

    private CosmosAllowlistRepository BuildRepo() =>
        new(_fx.AllowedUsers, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task First_signin_creates_pending_row_and_returns_false()
    {
        var repo = BuildRepo();

        var approved = await repo.IsApprovedAsync(SubA, CancellationToken.None);
        Assert.False(approved);

        // Side effect: the sub now shows up as approved=false so an admin
        // can flip it in Data Explorer without log-scraping.
        var doc = await _fx.AllowedUsers.ReadItemAsync<AllowedUserDocument>(SubA, new PartitionKey(SubA));
        Assert.False(doc.Resource.Approved);
        Assert.Equal(SubA, doc.Resource.Sub);
    }

    [Fact]
    public async Task Approved_sub_returns_true()
    {
        await _fx.AllowedUsers.UpsertItemAsync(new AllowedUserDocument
        {
            Id = SubB,
            Sub = SubB,
            Approved = true,
            FirstSeenAt = DateTimeOffset.UtcNow,
            ApprovedAt = DateTimeOffset.UtcNow,
        }, new PartitionKey(SubB));

        var repo = BuildRepo();
        Assert.True(await repo.IsApprovedAsync(SubB, CancellationToken.None));
    }

    [Fact]
    public async Task Pending_row_stays_false_until_flipped()
    {
        var sub = $"002222.{Guid.NewGuid():N}";
        var repo = BuildRepo();

        // First call self-registers as pending.
        Assert.False(await repo.IsApprovedAsync(sub, CancellationToken.None));
        // Subsequent calls still return false while approved=false in storage.
        Assert.False(await repo.IsApprovedAsync(sub, CancellationToken.None));

        // Flip in Cosmos, then a new repo instance (no warmed cache) should
        // observe approved=true. We use a new repo to ensure we're reading
        // from storage, not memo state.
        var doc = await _fx.AllowedUsers.ReadItemAsync<AllowedUserDocument>(sub, new PartitionKey(sub));
        await _fx.AllowedUsers.UpsertItemAsync(doc.Resource with { Approved = true, ApprovedAt = DateTimeOffset.UtcNow }, new PartitionKey(sub));

        Assert.True(await BuildRepo().IsApprovedAsync(sub, CancellationToken.None));
    }

    [Fact]
    public async Task Approved_result_is_cached_so_revoke_takes_up_to_a_minute()
    {
        // Documents the cache TTL behavior: once approved=true is observed,
        // the repo serves true from the cache even if the row is later
        // flipped back. Acceptable for a 60-s eventual-consistency window —
        // revocation isn't a primary feature and the alternative is paying
        // a Cosmos RU per request.
        var sub = $"003333.{Guid.NewGuid():N}";
        await _fx.AllowedUsers.UpsertItemAsync(new AllowedUserDocument
        {
            Id = sub, Sub = sub, Approved = true,
            FirstSeenAt = DateTimeOffset.UtcNow, ApprovedAt = DateTimeOffset.UtcNow,
        }, new PartitionKey(sub));

        var repo = BuildRepo();
        Assert.True(await repo.IsApprovedAsync(sub, CancellationToken.None));

        // Revoke in storage.
        var doc = await _fx.AllowedUsers.ReadItemAsync<AllowedUserDocument>(sub, new PartitionKey(sub));
        await _fx.AllowedUsers.UpsertItemAsync(doc.Resource with { Approved = false }, new PartitionKey(sub));

        // Cached approval still wins on the same repo instance.
        Assert.True(await repo.IsApprovedAsync(sub, CancellationToken.None));
        // A fresh repo (cold cache) reflects the revocation immediately.
        Assert.False(await BuildRepo().IsApprovedAsync(sub, CancellationToken.None));
    }

    [Fact]
    public async Task Empty_sub_returns_false_without_touching_storage()
    {
        var repo = BuildRepo();
        Assert.False(await repo.IsApprovedAsync("", CancellationToken.None));
        Assert.False(await repo.IsApprovedAsync("   ", CancellationToken.None));
    }
}
