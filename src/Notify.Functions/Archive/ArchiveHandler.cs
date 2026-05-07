using Notify.Shared.CloudEvents;
using Notify.Shared.Cosmos;
using Notify.Shared.Hashing;

namespace Notify.Functions.Archive;

// Pure archiving logic; the Function class is a thin EG-trigger shim around it.
// Cosmos is behind IArchiveSink so unit tests don't need the emulator.
public sealed class ArchiveHandler
{
    private readonly IArchiveSink _sink;

    public ArchiveHandler(IArchiveSink sink) => _sink = sink;

    public Task<ArchiveOutcome> HandleAsync(CloudEventEnvelope envelope, CancellationToken ct = default)
    {
        var docId = !string.IsNullOrEmpty(envelope.Data.DeduplicationKey)
            ? DedupKeyHasher.Hash(envelope.Data.Source, envelope.Data.DeduplicationKey)
            : envelope.Id;

        return _sink.StoreAsync(NotificationDocument.From(envelope, docId), ct);
    }
}
