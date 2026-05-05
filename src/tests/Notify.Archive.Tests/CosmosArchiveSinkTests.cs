using Microsoft.Azure.Cosmos;
using Notify.Archive.Archiving;
using Notify.Shared.CloudEvents;
using Notify.Shared.Cosmos;
using Notify.Shared.Hashing;

namespace Notify.Archive.Tests;

[Trait("Category", "Integration")]
public class CosmosArchiveSinkTests : IClassFixture<CosmosEmulatorFixture>
{
    private readonly CosmosEmulatorFixture _fx;

    public CosmosArchiveSinkTests(CosmosEmulatorFixture fx) => _fx = fx;

    private static CloudEventEnvelope NewEnvelope(string source, string? dedup = null)
    {
        var data = new NotifyCreatedV1
        {
            Source = source,
            Title = "t",
            Body = "b",
            DeduplicationKey = dedup,
            Timestamp = DateTimeOffset.UtcNow,
        };
        return CloudEventEnvelope.From(data, Guid.NewGuid(), data.Timestamp!.Value);
    }

    [Fact]
    public async Task Stores_document_under_source_partition()
    {
        var sink = new CosmosArchiveSink(_fx.Notifications);
        var handler = new ArchiveHandler(sink);
        var envelope = NewEnvelope("proj-shape");

        var outcome = await handler.HandleAsync(envelope);
        Assert.Equal(ArchiveOutcome.Created, outcome);

        var read = await _fx.Notifications.ReadItemAsync<NotificationDocument>(
            envelope.Id, new PartitionKey("proj-shape"));
        Assert.Equal("proj-shape", read.Resource.Source);
        Assert.Equal(envelope.Id, read.Resource.EnvelopeId);
        Assert.Equal("t", read.Resource.Title);
    }

    [Fact]
    public async Task Same_dedup_key_produces_exactly_one_document()
    {
        var sink = new CosmosArchiveSink(_fx.Notifications);
        var handler = new ArchiveHandler(sink);

        var first  = await handler.HandleAsync(NewEnvelope("proj-dedup", dedup: "k"));
        var second = await handler.HandleAsync(NewEnvelope("proj-dedup", dedup: "k"));

        Assert.Equal(ArchiveOutcome.Created, first);
        Assert.Equal(ArchiveOutcome.DuplicateIgnored, second);

        var query = _fx.Notifications.GetItemQueryIterator<NotificationDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.source = @s")
                .WithParameter("@s", "proj-dedup"));
        var docs = new List<NotificationDocument>();
        while (query.HasMoreResults)
            docs.AddRange(await query.ReadNextAsync());

        var doc = Assert.Single(docs);
        Assert.Equal(DedupKeyHasher.Hash("proj-dedup", "k"), doc.Id);
    }

    [Fact]
    public async Task Different_sources_with_same_dedup_key_produce_distinct_documents()
    {
        var sink = new CosmosArchiveSink(_fx.Notifications);
        var handler = new ArchiveHandler(sink);

        var a = await handler.HandleAsync(NewEnvelope("proj-a", dedup: "shared"));
        var b = await handler.HandleAsync(NewEnvelope("proj-b", dedup: "shared"));

        Assert.Equal(ArchiveOutcome.Created, a);
        Assert.Equal(ArchiveOutcome.Created, b);
    }
}
