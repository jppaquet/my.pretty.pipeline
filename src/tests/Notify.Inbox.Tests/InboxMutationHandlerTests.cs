using Notify.Functions.Inbox;

namespace Notify.Functions.Inbox.Tests;

public class InboxMutationHandlerTests
{
    private const string User = "001234.abcdef.0099";
    private const string Source = "home-pipeline";
    private const string BaseId = "55555555-4444-7222-9111-000000000001";
    private static string FullId => $"{BaseId}:{User}";

    private static (InboxMutationHandler handler, InMemoryInboxMutator mutator) NewHandler()
    {
        var mutator = new InMemoryInboxMutator();
        return (new InboxMutationHandler(mutator), mutator);
    }

    [Theory]
    [InlineData(InboxMutationHandler.Action.MarkRead, "isRead")]
    [InlineData(InboxMutationHandler.Action.MarkHidden, "isHidden")]
    public async Task Happy_path_marks_field_and_returns_NoContent(InboxMutationHandler.Action action, string field)
    {
        var (handler, mutator) = NewHandler();
        var result = await handler.HandleAsync(User, Source, FullId, action);
        Assert.IsType<InboxMutationResult.NoContent>(result);
        var call = Assert.Single(mutator.Calls);
        Assert.Equal(Source, call.Source);
        Assert.Equal(FullId, call.Id);
        Assert.Equal(field, call.Field);
    }

    [Fact]
    public async Task Missing_userId_returns_Unauthorized()
    {
        var (handler, mutator) = NewHandler();
        var result = await handler.HandleAsync(userId: null, Source, FullId, InboxMutationHandler.Action.MarkRead);
        Assert.IsType<InboxMutationResult.Unauthorized>(result);
        Assert.Empty(mutator.Calls);
    }

    [Fact]
    public async Task Empty_userId_returns_Unauthorized()
    {
        var (handler, _) = NewHandler();
        var result = await handler.HandleAsync("   ", Source, FullId, InboxMutationHandler.Action.MarkRead);
        Assert.IsType<InboxMutationResult.Unauthorized>(result);
    }

    [Fact]
    public async Task Missing_source_returns_BadRequest()
    {
        var (handler, _) = NewHandler();
        var result = await handler.HandleAsync(User, source: null, FullId, InboxMutationHandler.Action.MarkRead);
        var br = Assert.IsType<InboxMutationResult.BadRequest>(result);
        Assert.Contains(br.Failures, f => f.Field == "source");
    }

    [Fact]
    public async Task Missing_id_returns_BadRequest()
    {
        var (handler, _) = NewHandler();
        var result = await handler.HandleAsync(User, Source, id: null, InboxMutationHandler.Action.MarkRead);
        var br = Assert.IsType<InboxMutationResult.BadRequest>(result);
        Assert.Contains(br.Failures, f => f.Field == "id");
    }

    [Theory]
    [InlineData("no-colon")]
    [InlineData(":only-suffix")]
    [InlineData("only-prefix:")]
    public async Task Malformed_id_returns_BadRequest(string id)
    {
        var (handler, _) = NewHandler();
        var result = await handler.HandleAsync(User, Source, id, InboxMutationHandler.Action.MarkRead);
        var br = Assert.IsType<InboxMutationResult.BadRequest>(result);
        Assert.Contains(br.Failures, f => f.Field == "id");
    }

    [Fact]
    public async Task Id_user_suffix_mismatch_returns_Forbidden()
    {
        var (handler, mutator) = NewHandler();
        var otherUserId = $"{BaseId}:999999.someone-else.abcd";
        var result = await handler.HandleAsync(User, Source, otherUserId, InboxMutationHandler.Action.MarkRead);
        Assert.IsType<InboxMutationResult.Forbidden>(result);
        Assert.Empty(mutator.Calls);
    }

    [Fact]
    public async Task NotFound_from_mutator_surfaces_as_NotFound()
    {
        var (handler, mutator) = NewHandler();
        // Existing is non-empty + doesn't contain the row → mutator returns NotFound.
        mutator.Existing.Add((Source, "some-other-id:nope"));
        var result = await handler.HandleAsync(User, Source, FullId, InboxMutationHandler.Action.MarkRead);
        Assert.IsType<InboxMutationResult.NotFound>(result);
    }

    [Fact]
    public async Task Repeated_call_is_idempotent()
    {
        var (handler, mutator) = NewHandler();
        var r1 = await handler.HandleAsync(User, Source, FullId, InboxMutationHandler.Action.MarkRead);
        var r2 = await handler.HandleAsync(User, Source, FullId, InboxMutationHandler.Action.MarkRead);
        Assert.IsType<InboxMutationResult.NoContent>(r1);
        Assert.IsType<InboxMutationResult.NoContent>(r2);
        Assert.Equal(2, mutator.Calls.Count);
    }
}
