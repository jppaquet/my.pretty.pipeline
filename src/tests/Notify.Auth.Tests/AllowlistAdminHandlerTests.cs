using Microsoft.Azure.Cosmos;
using Notify.Functions.Admin;
using Notify.Functions.Auth;

namespace Notify.Auth.Tests;

// Mutating admin operations against the same `allowedUsers` container the
// repository writes to. The repository's self-registration side-effect is
// out of scope here — these tests start from a known doc state and verify
// approve / revoke behave correctly + idempotently.
[Trait("Category", "Integration")]
public class AllowlistAdminHandlerTests : IClassFixture<CosmosEmulatorFixture>
{
    private readonly CosmosEmulatorFixture _fx;

    public AllowlistAdminHandlerTests(CosmosEmulatorFixture fx) => _fx = fx;

    private AllowlistAdminHandler BuildHandler() => new(_fx.AllowedUsers);

    private async Task<string> SeedPendingAsync()
    {
        var sub = $"010101.{Guid.NewGuid():N}";
        await _fx.AllowedUsers.UpsertItemAsync(new AllowedUserDocument
        {
            Id = sub, Sub = sub, Approved = false,
            FirstSeenAt = DateTimeOffset.UtcNow,
        }, new PartitionKey(sub));
        return sub;
    }

    [Fact]
    public async Task Approve_flips_pending_to_approved_and_stamps_approvedAt()
    {
        var sub = await SeedPendingAsync();
        var handler = BuildHandler();

        var doc = await handler.ApproveAsync(sub);

        Assert.NotNull(doc);
        Assert.True(doc!.Approved);
        Assert.NotNull(doc.ApprovedAt);
    }

    [Fact]
    public async Task Revoke_flips_approved_to_pending_and_clears_approvedAt()
    {
        var sub = await SeedPendingAsync();
        var handler = BuildHandler();
        await handler.ApproveAsync(sub);

        var doc = await handler.RevokeAsync(sub);

        Assert.NotNull(doc);
        Assert.False(doc!.Approved);
        Assert.Null(doc.ApprovedAt);
    }

    [Fact]
    public async Task Approve_is_idempotent()
    {
        var sub = await SeedPendingAsync();
        var handler = BuildHandler();

        var first = await handler.ApproveAsync(sub);
        var second = await handler.ApproveAsync(sub);

        Assert.True(first!.Approved);
        Assert.True(second!.Approved);
        // approvedAt advances; treat ETag-style strict equality as
        // out-of-scope but verify both calls succeed without error.
    }

    [Fact]
    public async Task Approve_on_missing_sub_returns_null()
    {
        var handler = BuildHandler();
        var doc = await handler.ApproveAsync("000000.never-signed-in-" + Guid.NewGuid().ToString("N"));
        Assert.Null(doc);
    }

    [Fact]
    public async Task Revoke_on_missing_sub_returns_null()
    {
        var handler = BuildHandler();
        var doc = await handler.RevokeAsync("000000.never-signed-in-" + Guid.NewGuid().ToString("N"));
        Assert.Null(doc);
    }

    [Fact]
    public async Task Empty_sub_returns_null_without_touching_storage()
    {
        var handler = BuildHandler();
        Assert.Null(await handler.ApproveAsync(""));
        Assert.Null(await handler.RevokeAsync("   "));
    }

    [Fact]
    public async Task List_returns_seeded_rows_newest_first()
    {
        var handler = BuildHandler();
        var oldSub = await SeedPendingAsync();
        await Task.Delay(50); // ensure firstSeenAt differs
        var newSub = await SeedPendingAsync();
        // Bump newSub's firstSeenAt to guarantee ordering even on the
        // emulator where timestamp resolution can collapse.
        var existing = await _fx.AllowedUsers.ReadItemAsync<AllowedUserDocument>(newSub, new PartitionKey(newSub));
        await _fx.AllowedUsers.UpsertItemAsync(
            existing.Resource with { FirstSeenAt = DateTimeOffset.UtcNow.AddSeconds(10) },
            new PartitionKey(newSub));

        var items = await handler.ListAsync();
        var index = items.Select((d, i) => (d, i)).ToDictionary(x => x.d.Sub, x => x.i);

        Assert.True(index.ContainsKey(oldSub));
        Assert.True(index.ContainsKey(newSub));
        Assert.True(index[newSub] < index[oldSub], "newer sub should appear before older");
    }
}
