using Notify.Functions.Inbox;
using Notify.Shared.Cosmos;

namespace Notify.Functions.Inbox.Tests;

public class InboxHandlerTests
{
    private const string UserA = "001234.aaaaaaaa";
    private const string UserB = "001234.bbbbbbbb";

    private static NotificationDocument Doc(string userId, string source, DateTimeOffset ts, string? title = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Source = source,
        UserId = userId,
        Title = title ?? "t",
        Body = "b",
        Timestamp = ts,
        EnvelopeId = Guid.NewGuid().ToString(),
    };

    [Fact]
    public async Task Returns_items_newest_first_for_authenticated_user()
    {
        var fake = new InMemoryInboxQuery();
        var t0 = DateTimeOffset.UtcNow;
        fake.Stored.AddRange(new[]
        {
            Doc(UserA, "a", t0.AddMinutes(-1), "old"),
            Doc(UserA, "b", t0,                "new"),
            Doc(UserA, "a", t0.AddSeconds(-30),"mid"),
        });

        var handler = new InboxHandler(fake);
        var result = await handler.HandleAsync(UserA, new InboxQueryRequest());

        var ok = Assert.IsType<InboxResult.Ok>(result);
        Assert.Collection(ok.Items,
            d => Assert.Equal("new", d.Title),
            d => Assert.Equal("mid", d.Title),
            d => Assert.Equal("old", d.Title));
        Assert.Equal(UserA, fake.LastUserId);
    }

    [Fact]
    public async Task Filters_out_other_users_documents()
    {
        var fake = new InMemoryInboxQuery();
        var t0 = DateTimeOffset.UtcNow;
        fake.Stored.AddRange(new[]
        {
            Doc(UserA, "a", t0, "mine"),
            Doc(UserB, "a", t0, "theirs"),
        });

        var handler = new InboxHandler(fake);
        var result = await handler.HandleAsync(UserA, new InboxQueryRequest());

        var ok = Assert.IsType<InboxResult.Ok>(result);
        var doc = Assert.Single(ok.Items);
        Assert.Equal("mine", doc.Title);
    }

    [Fact]
    public async Task Forwards_source_filter_to_query()
    {
        var fake = new InMemoryInboxQuery();
        fake.Stored.AddRange(new[]
        {
            Doc(UserA, "a", DateTimeOffset.UtcNow),
            Doc(UserA, "b", DateTimeOffset.UtcNow),
        });

        var handler = new InboxHandler(fake);
        var result = await handler.HandleAsync(UserA, new InboxQueryRequest { Source = "a" });

        var ok = Assert.IsType<InboxResult.Ok>(result);
        var doc = Assert.Single(ok.Items);
        Assert.Equal("a", doc.Source);
        Assert.Equal("a", fake.LastSource);
    }

    [Fact]
    public async Task Forwards_continuation_token_round_trip()
    {
        var fake = new InMemoryInboxQuery { NextContinuationToken = "next-page-token" };

        var handler = new InboxHandler(fake);
        var result = await handler.HandleAsync(UserA, new InboxQueryRequest { ContinuationToken = "incoming" });

        var ok = Assert.IsType<InboxResult.Ok>(result);
        Assert.Equal("incoming", fake.LastContinuationToken);
        Assert.Equal("next-page-token", ok.ContinuationToken);
    }

    [Fact]
    public async Task Default_limit_is_50()
    {
        var fake = new InMemoryInboxQuery();
        var handler = new InboxHandler(fake);

        await handler.HandleAsync(UserA, new InboxQueryRequest());

        Assert.Equal(InboxOptions.DefaultLimit, fake.LastLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(InboxOptions.MaxLimit + 1)]
    public async Task Bad_limit_yields_BadRequest(int limit)
    {
        var fake = new InMemoryInboxQuery();
        var handler = new InboxHandler(fake);

        var result = await handler.HandleAsync(UserA, new InboxQueryRequest { Limit = limit });

        var bad = Assert.IsType<InboxResult.BadRequest>(result);
        Assert.Contains(bad.Failures, f => f.Field == "limit");
    }

    [Fact]
    public async Task Source_over_max_length_yields_BadRequest()
    {
        var fake = new InMemoryInboxQuery();
        var handler = new InboxHandler(fake);

        var result = await handler.HandleAsync(UserA, new InboxQueryRequest
        {
            Source = new string('x', InboxOptions.MaxSourceLength + 1),
        });

        var bad = Assert.IsType<InboxResult.BadRequest>(result);
        Assert.Contains(bad.Failures, f => f.Field == "source");
    }

    [Fact]
    public async Task Validation_failures_short_circuit_query()
    {
        var fake = new InMemoryInboxQuery();
        fake.Stored.Add(Doc(UserA, "a", DateTimeOffset.UtcNow));
        var handler = new InboxHandler(fake);

        await handler.HandleAsync(UserA, new InboxQueryRequest { Limit = 0 });

        Assert.Null(fake.LastSource);
        Assert.Equal(0, fake.LastLimit);
        Assert.Null(fake.LastUserId);
    }
}
