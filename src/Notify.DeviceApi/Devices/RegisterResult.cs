using Notify.Shared.Validation;

namespace Notify.DeviceApi.Devices;

// Discriminated result of a single device-registration attempt; the Function
// maps it to the right HTTP status. Mirrors IngestResult in Notify.IngestionApi.
public abstract record RegisterResult
{
    public sealed record Accepted(string InstallationId) : RegisterResult;
    public sealed record BadRequest(IReadOnlyList<ValidationFailure> Failures) : RegisterResult;
    public sealed record PayloadTooLarge(int LimitBytes) : RegisterResult;
}
