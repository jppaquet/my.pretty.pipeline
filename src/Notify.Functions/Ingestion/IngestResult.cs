using Notify.Shared.Validation;

namespace Notify.Functions.Ingestion;

// Discriminated result of a single ingestion attempt; the Function maps it
// to the right HTTP status. Defined as an abstract record so tests can match
// directly via `Assert.IsType<IngestResult.Accepted>(result)`.
public abstract record IngestResult
{
    public sealed record Accepted(string Id) : IngestResult;
    public sealed record AcceptedBatch(IReadOnlyList<string> Ids) : IngestResult;
    public sealed record BadRequest(IReadOnlyList<ValidationFailure> Failures) : IngestResult;
    public sealed record Unauthorized : IngestResult;
    public sealed record PayloadTooLarge(int LimitBytes) : IngestResult;
    public sealed record UnsupportedMediaType(string Message) : IngestResult;
}
