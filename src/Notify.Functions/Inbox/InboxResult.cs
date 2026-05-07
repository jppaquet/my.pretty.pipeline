using Notify.Shared.Cosmos;
using Notify.Shared.Validation;

namespace Notify.Functions.Inbox;

public abstract record InboxResult
{
    public sealed record Ok(IReadOnlyList<NotificationDocument> Items, string? ContinuationToken) : InboxResult;
    public sealed record BadRequest(IReadOnlyList<ValidationFailure> Failures) : InboxResult;
}
