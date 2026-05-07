namespace Notify.Functions.Inbox;

public sealed record InboxQueryRequest
{
    public string? Source { get; init; }
    public int Limit { get; init; } = InboxOptions.DefaultLimit;
    public string? ContinuationToken { get; init; }
}
