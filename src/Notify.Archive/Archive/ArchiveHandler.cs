using Notify.Shared.CloudEvents;
using Notify.Shared.Cosmos;
using Notify.Shared.Hashing;

namespace Notify.Archive.Archive;

// Pure archival logic. Function class is a thin EG trigger shim.
public sealed class ArchiveHandler
{
    private readonly INotificationStore _store;

    public ArchiveHandler(INotificationStore store) => _store = store;

    public async Task<ArchiveResult> HandleAsync(CloudEventEnvelope envelope, CancellationToken ct = default)
    {
        var data = envelope.Data;

        // Dedup-derived id when the producer supplied a key, else the envelope's UUID.
        var id = string.IsNullOrEmpty(data.DeduplicationKey)
            ? envelope.Id
            : DedupKeyHasher.Hash(data.Source, data.DeduplicationKey);

        var doc = new NotificationDocument
        {
            Id = id,
            Source = data.Source,
            Type = data.Type,
            Title = data.Title,
            Body = data.Body,
            Priority = data.Priority,
            Tags = data.Tags,
            Deeplink = data.Deeplink,
            Metadata = data.Metadata,
            DeduplicationKey = data.DeduplicationKey,
            Timestamp = data.Timestamp ?? envelope.Time,
            ReadAt = null,
        };

        var created = await _store.TryCreateAsync(doc, ct);
        return created ? new ArchiveResult.Created(id) : new ArchiveResult.AlreadyExists(id);
    }
}
