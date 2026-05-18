using Notify.Shared.Validation;

namespace Notify.Functions.Inbox;

// Pure mutation logic for per-user inbox rows. Owns the security boundary:
// the doc id is `{baseId}:{userId}` (composed by Archive), so we require the
// suffix to match the authenticated user before touching Cosmos. Bypassing
// this would let any token holder flip another user's `isRead`/`isHidden`.
public sealed class InboxMutationHandler
{
    public enum Action { MarkRead, MarkHidden }

    private readonly IInboxMutator _mutator;

    public InboxMutationHandler(IInboxMutator mutator) => _mutator = mutator;

    public async Task<InboxMutationResult> HandleAsync(
        string? userId,
        string? source,
        string? id,
        Action action,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new InboxMutationResult.Unauthorized();

        var failures = new List<ValidationFailure>();
        if (string.IsNullOrWhiteSpace(source))
            failures.Add(new ValidationFailure("source", "required"));
        if (string.IsNullOrWhiteSpace(id))
            failures.Add(new ValidationFailure("id", "required"));
        if (failures.Count > 0)
            return new InboxMutationResult.BadRequest(failures);

        // Id format: `{baseId}:{userId}`. Split on the LAST `:` since the
        // baseId may itself contain `:` (envelope ids are guids — no colons —
        // and dedup-key hashes are hex, but be defensive about future formats).
        var sep = id!.LastIndexOf(':');
        if (sep <= 0 || sep == id.Length - 1)
            return new InboxMutationResult.BadRequest(new[] { new ValidationFailure("id", "must be of the form '<baseId>:<userId>'") });

        var idUserId = id[(sep + 1)..];
        if (!string.Equals(idUserId, userId, StringComparison.Ordinal))
            return new InboxMutationResult.Forbidden();

        var outcome = action switch
        {
            Action.MarkRead   => await _mutator.MarkReadAsync(source!, id, ct),
            Action.MarkHidden => await _mutator.MarkHiddenAsync(source!, id, ct),
            _ => throw new InvalidOperationException($"Unknown action: {action}"),
        };

        return outcome switch
        {
            InboxMutateOutcome.Updated  => new InboxMutationResult.NoContent(),
            InboxMutateOutcome.NotFound => new InboxMutationResult.NotFound(),
            _ => throw new InvalidOperationException($"Unknown outcome: {outcome}"),
        };
    }
}
