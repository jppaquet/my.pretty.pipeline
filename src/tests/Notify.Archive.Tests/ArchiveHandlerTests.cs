using Notify.Archive.Archiving;
using Notify.Shared;
using Notify.Shared.CloudEvents;
using Notify.Shared.Hashing;

namespace Notify.Archive.Tests;

public class ArchiveHandlerTests
{
    private static CloudEventEnvelope NewEnvelope(string source = "home-pipeline", string? dedup = null, string? envelopeId = null)
    {
        var data = new NotifyCreatedV1
        {
            Source = source,
            Title = "Backup failed",
            Body = "rsync exited 12",
            DeduplicationKey = dedup,
            Id = envelopeId,
            Timestamp = DateTimeOffset.UtcNow,
        };
        return CloudEventEnvelope.From(data, Guid.Parse(envelopeId ?? Guid.NewGuid().ToString()), DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Without_dedup_key_uses_envelope_id_as_doc_id()
    {
        var sink = new InMemoryArchiveSink();
        var handler = new ArchiveHandler(sink);

        var envelope = NewEnvelope();
        var outcome = await handler.HandleAsync(envelope);

        Assert.Equal(ArchiveOutcome.Created, outcome);
        var doc = Assert.Single(sink.Stored.Values);
        Assert.Equal(envelope.Id, doc.Id);
        Assert.Equal(envelope.Id, doc.EnvelopeId);
        Assert.Equal("home-pipeline", doc.Source);
    }

    [Fact]
    public async Task With_dedup_key_uses_sha256_hash_as_doc_id()
    {
        var sink = new InMemoryArchiveSink();
        var handler = new ArchiveHandler(sink);

        var envelope = NewEnvelope(dedup: "backup-2026-04-28");
        var outcome = await handler.HandleAsync(envelope);

        Assert.Equal(ArchiveOutcome.Created, outcome);
        var doc = Assert.Single(sink.Stored.Values);
        Assert.Equal(DedupKeyHasher.Hash("home-pipeline", "backup-2026-04-28"), doc.Id);
        Assert.Equal(envelope.Id, doc.EnvelopeId);
    }

    [Fact]
    public async Task Same_dedup_key_twice_collapses_to_one_document()
    {
        var sink = new InMemoryArchiveSink();
        var handler = new ArchiveHandler(sink);

        var first  = await handler.HandleAsync(NewEnvelope(dedup: "k"));
        var second = await handler.HandleAsync(NewEnvelope(dedup: "k"));

        Assert.Equal(ArchiveOutcome.Created, first);
        Assert.Equal(ArchiveOutcome.DuplicateIgnored, second);
        Assert.Single(sink.Stored);
    }

    [Fact]
    public async Task Doc_carries_full_payload()
    {
        var sink = new InMemoryArchiveSink();
        var handler = new ArchiveHandler(sink);

        var data = new NotifyCreatedV1
        {
            Source = "home-pipeline",
            Title = "t",
            Body = "b",
            Type = "alert",
            Priority = Priority.High,
            Tags = new[] { "pi-01" },
            Deeplink = "https://example/a",
            Timestamp = DateTimeOffset.UtcNow,
        };
        var envelope = CloudEventEnvelope.From(data, Guid.NewGuid(), data.Timestamp!.Value);

        await handler.HandleAsync(envelope);

        var doc = Assert.Single(sink.Stored.Values);
        Assert.Equal("alert", doc.Type);
        Assert.Equal(Priority.High, doc.Priority);
        Assert.Equal(new[] { "pi-01" }, doc.Tags);
        Assert.Equal("https://example/a", doc.Deeplink);
    }
}
