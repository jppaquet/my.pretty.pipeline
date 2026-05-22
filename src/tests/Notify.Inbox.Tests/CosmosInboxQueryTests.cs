using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Notify.Functions.Inbox;
using Notify.Shared.Cosmos;

namespace Notify.Functions.Inbox.Tests;

[Trait("Category", "Integration")]
public class CosmosInboxQueryTests : IClassFixture<CosmosEmulatorFixture>
{
    private const string UserA = "001234.aaaaaaaa";
    private const string UserB = "001234.bbbbbbbb";

    private readonly CosmosEmulatorFixture _fx;

    public CosmosInboxQueryTests(CosmosEmulatorFixture fx) => _fx = fx;

    private static NotificationDocument Doc(string userId, string source, DateTimeOffset ts) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Source = source,
        UserId = userId,
        Title = $"{source}@{ts:O}",
        Body = "b",
        Timestamp = ts,
        EnvelopeId = Guid.NewGuid().ToString(),
    };

    private async Task SeedAsync(params NotificationDocument[] docs)
    {
        foreach (var d in docs)
            await _fx.Notifications.CreateItemAsync(d, new PartitionKey(d.Source));
    }

    [Fact]
    public async Task Returns_all_partitions_newest_first_when_no_source_filter()
    {
        var t0 = DateTimeOffset.UtcNow;
        var older  = Doc(UserA, "multi-a", t0.AddMinutes(-2));
        var newer  = Doc(UserA, "multi-b", t0);
        var middle = Doc(UserA, "multi-a", t0.AddMinutes(-1));
        await SeedAsync(older, newer, middle);

        var query = new CosmosInboxQuery(_fx.Notifications, NullLogger<CosmosInboxQuery>.Instance);
        var page = await query.QueryAsync(userId: UserA, source: null, limit: 50, continuationToken: null);

        var byOurSources = page.Items.Where(d => d.Source.StartsWith("multi-", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, byOurSources.Count);
        Assert.Equal(newer.Id, byOurSources[0].Id);
        Assert.Equal(middle.Id, byOurSources[1].Id);
        Assert.Equal(older.Id, byOurSources[2].Id);
    }

    [Fact]
    public async Task Filters_out_other_users_documents()
    {
        var t0 = DateTimeOffset.UtcNow;
        await SeedAsync(
            Doc(UserA, "tenant-a", t0),
            Doc(UserB, "tenant-a", t0));

        var query = new CosmosInboxQuery(_fx.Notifications, NullLogger<CosmosInboxQuery>.Instance);
        var page = await query.QueryAsync(userId: UserA, source: "tenant-a", limit: 50, continuationToken: null);

        var doc = Assert.Single(page.Items);
        Assert.Equal(UserA, doc.UserId);
    }

    [Fact]
    public async Task Source_filter_returns_only_that_partition()
    {
        var t0 = DateTimeOffset.UtcNow;
        await SeedAsync(
            Doc(UserA, "filter-a", t0),
            Doc(UserA, "filter-b", t0));

        var query = new CosmosInboxQuery(_fx.Notifications, NullLogger<CosmosInboxQuery>.Instance);
        var page = await query.QueryAsync(userId: UserA, source: "filter-a", limit: 50, continuationToken: null);

        var doc = Assert.Single(page.Items);
        Assert.Equal("filter-a", doc.Source);
    }

    [Fact]
    public async Task Limit_caps_page_size_and_yields_continuation_token()
    {
        var t0 = DateTimeOffset.UtcNow;
        await SeedAsync(
            Doc(UserA, "page-a", t0.AddSeconds(-3)),
            Doc(UserA, "page-a", t0.AddSeconds(-2)),
            Doc(UserA, "page-a", t0.AddSeconds(-1)));

        var query = new CosmosInboxQuery(_fx.Notifications, NullLogger<CosmosInboxQuery>.Instance);
        var first = await query.QueryAsync(userId: UserA, source: "page-a", limit: 1, continuationToken: null);

        Assert.Single(first.Items);
        Assert.False(string.IsNullOrEmpty(first.ContinuationToken));

        var second = await query.QueryAsync(userId: UserA, source: "page-a", limit: 1, continuationToken: first.ContinuationToken);
        Assert.Single(second.Items);
        Assert.NotEqual(first.Items[0].Id, second.Items[0].Id);
    }
}
