using Notify.Functions.Archive;
using Notify.Shared.Cosmos;

namespace Notify.Functions.Archive.Tests;

internal sealed class InMemoryArchiveSink : IArchiveSink
{
    public Dictionary<(string Source, string Id), NotificationDocument> Stored { get; } = new();

    public Task<ArchiveOutcome> StoreAsync(NotificationDocument document, CancellationToken ct = default)
    {
        var key = (document.Source, document.Id);
        if (!Stored.TryAdd(key, document))
            return Task.FromResult(ArchiveOutcome.DuplicateIgnored);
        return Task.FromResult(ArchiveOutcome.Created);
    }
}
