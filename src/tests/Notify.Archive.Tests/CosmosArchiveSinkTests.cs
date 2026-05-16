using Microsoft.Azure.Cosmos;
using Notify.Functions.Archive;
using Notify.Shared.CloudEvents;
using Notify.Shared.Cosmos;
using Notify.Shared.Hashing;

namespace Notify.Functions.Archive.Tests;

[Trait("Category", "Integration")]
public class CosmosArchiveSinkTests : IClassFixture<CosmosEmulatorFixture>
{
    private const string UserA = "001234.aaaaaaaa";
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

    private ArchiveHandler BuildHandler(params string[] userIds)
    {
        var sink = new CosmosArchiveSink(_fx.Notifications);
        var users = new InMemoryUserDirectory();
        users.UserIds.AddRange(userIds);
        return new ArchiveHandler(sink, users);
    }

    [Fact]
    public async Task Stores_per_user_document_under_source_partition()
    {
        var handler = BuildHandler(UserA);
        var envelope = NewEnvelope("proj-shape");

        var result = await handler.HandleAsync(envelope);
        Assert.Equal(1, result.Created);

        var read = await _fx.Notifications.ReadItemAsync<NotificationDocument>(
            $"{envelope.Id}:{UserA}", new PartitionKey("proj-shape"));
        Assert.Equal("proj-shape", read.Resource.Source);
        Assert.Equal(envelope.Id, read.Resource.EnvelopeId);
        Assert.Equal(UserA, read.Resource.UserId);
        Assert.Equal("t", read.Resource.Title);
    }

    [Fact]
    public async Task Same_dedup_key_per_user_produces_exactly_one_document()
    {
        var handler = BuildHandler(UserA);

        var first  = await handler.HandleAsync(NewEnvelope("proj-dedup", dedup: "k"));
        var second = await handler.HandleAsync(NewEnvelope("proj-dedup", dedup: "k"));

        Assert.Equal(1, first.Created);
        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Duplicates);

        var query = _fx.Notifications.GetItemQueryIterator<NotificationDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.source = @s")
                .WithParameter("@s", "proj-dedup"));
        var docs = new List<NotificationDocument>();
        while (query.HasMoreResults)
            docs.AddRange(await query.ReadNextAsync());

        var doc = Assert.Single(docs);
        Assert.Equal($"{DedupKeyHasher.Hash("proj-dedup", "k")}:{UserA}", doc.Id);
    }

    [Fact]
    public async Task Different_sources_with_same_dedup_key_produce_distinct_documents()
    {
        var handler = BuildHandler(UserA);

        var a = await handler.HandleAsync(NewEnvelope("proj-a", dedup: "shared"));
        var b = await handler.HandleAsync(NewEnvelope("proj-b", dedup: "shared"));

        Assert.Equal(1, a.Created);
        Assert.Equal(1, b.Created);
    }
}
