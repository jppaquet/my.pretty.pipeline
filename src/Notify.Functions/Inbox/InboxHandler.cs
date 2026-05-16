using Notify.Shared.Validation;

namespace Notify.Functions.Inbox;

// Pure inbox-read logic; the Function class is a thin HTTP shim around it.
// Cosmos is behind IInboxQuery so unit tests don't need the emulator.
//
// The userId is the authenticated Sign-in-with-Apple `sub` extracted from
// the request's Bearer token by JwtAuthMiddleware. It is mandatory here:
// without an authenticated user the handler returns BadRequest so the
// Function-layer wrapper can map it to 401. The inbox query then scopes
// the Cosmos predicate to `c.userId = @user` so a token holder only
// sees their own notifications (closes the H2 finding).
public sealed class InboxHandler
{
    private readonly IInboxQuery _query;

    public InboxHandler(IInboxQuery query) => _query = query;

    public async Task<InboxResult> HandleAsync(string userId, InboxQueryRequest request, CancellationToken ct = default)
    {
        var failures = new List<ValidationFailure>();

        if (request.Limit < 1 || request.Limit > InboxOptions.MaxLimit)
            failures.Add(new ValidationFailure("limit", $"must be between 1 and {InboxOptions.MaxLimit}"));

        if (request.Source is { Length: > InboxOptions.MaxSourceLength })
            failures.Add(new ValidationFailure("source", $"max {InboxOptions.MaxSourceLength} chars"));

        if (failures.Count > 0)
            return new InboxResult.BadRequest(failures);

        var page = await _query.QueryAsync(userId, request.Source, request.Limit, request.ContinuationToken, ct);
        return new InboxResult.Ok(page.Items, page.ContinuationToken);
    }
}
