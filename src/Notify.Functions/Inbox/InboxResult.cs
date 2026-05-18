using Notify.Shared.Cosmos;
using Notify.Shared.Validation;

namespace Notify.Functions.Inbox;

public abstract record InboxResult
{
    public sealed record Ok(IReadOnlyList<NotificationDocument> Items, string? ContinuationToken) : InboxResult;
    public sealed record BadRequest(IReadOnlyList<ValidationFailure> Failures) : InboxResult;
}

// Result of a mutation against a per-user inbox row (POST /read, DELETE).
// Separate from the read result so the HTTP shim maps each shape directly.
public abstract record InboxMutationResult
{
    public sealed record NoContent : InboxMutationResult;
    public sealed record BadRequest(IReadOnlyList<ValidationFailure> Failures) : InboxMutationResult;
    public sealed record Unauthorized : InboxMutationResult;
    public sealed record Forbidden : InboxMutationResult;
    public sealed record NotFound : InboxMutationResult;
}
