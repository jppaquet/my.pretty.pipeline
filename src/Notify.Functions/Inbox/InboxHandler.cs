using Notify.Shared.Validation;

namespace Notify.Functions.Inbox;

// Pure inbox-read logic; the Function class is a thin HTTP shim around it.
// Cosmos is behind IInboxQuery so unit tests don't need the emulator.
public sealed class InboxHandler
{
    private readonly IInboxQuery _query;

    public InboxHandler(IInboxQuery query) => _query = query;

    public async Task<InboxResult> HandleAsync(InboxQueryRequest request, CancellationToken ct = default)
    {
        var failures = new List<ValidationFailure>();

        if (request.Limit < 1 || request.Limit > InboxOptions.MaxLimit)
            failures.Add(new ValidationFailure("limit", $"must be between 1 and {InboxOptions.MaxLimit}"));

        if (request.Source is { Length: > InboxOptions.MaxSourceLength })
            failures.Add(new ValidationFailure("source", $"max {InboxOptions.MaxSourceLength} chars"));

        if (failures.Count > 0)
            return new InboxResult.BadRequest(failures);

        var page = await _query.QueryAsync(request.Source, request.Limit, request.ContinuationToken, ct);
        return new InboxResult.Ok(page.Items, page.ContinuationToken);
    }
}
