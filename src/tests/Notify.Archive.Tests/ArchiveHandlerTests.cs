using Notify.Archive.Archive;
using Notify.Shared;
using Notify.Shared.CloudEvents;
using Notify.Shared.Cosmos;
using Notify.Shared.Hashing;

namespace Notify.Archive.Tests;

internal sealed class InMemoryNotificationStore : INotificationStore
{
    public Dictionary<string, NotificationDocument> Items { get; } = new();
    public int CreateAttempts { get; private set; }
    public int Conflicts { get; private set; }

    public Task<bool> TryCreateAsync(NotificationDocument document, CancellationToken ct = default)
    {
        CreateAttempts++;
        if (Items.ContainsKey(document.Id))
        {
            Conflicts++;
            return Task.FromResult(false);
        }
        Items[document.Id] = document;
        return Task.FromResult(true);
    }
}

public class ArchiveHandlerTests
{
    private static CloudEventEnvelope EnvelopeFor(NotifyCreatedV1 data, Guid? id = null, DateTimeOffset? time = null)
        => CloudEventEnvelope.From(data, id ?? Guid.CreateVersion7(), time ?? DateTimeOffset.UtcNow);

    private static NotifyCreatedV1 Sample(string? dedupKey = null) => new()
    {
        Source = "home-pipeline",
        Title = "Backup failed",
        Body = "rsync exited 12",
        Priority = Priority.High,
        DeduplicationKey = dedupKey,
    };

    [Fact]
    public async Task Without_dedup_key_uses_envelope_id_as_doc_id()
    {
        var store = new InMemoryNotificationStore();
        var handler = new ArchiveHandler(store);
        var envelope = EnvelopeFor(Sample());

        var result = await handler.HandleAsync(envelope);

        var created = Assert.IsType<ArchiveResult.Created>(result);
        Assert.Equal(envelope.Id, created.Id);
        Assert.True(store.Items.ContainsKey(envelope.Id));
    }

    [Fact]
    public async Task With_dedup_key_derives_id_from_source_and_key()
    {
        var store = new InMemoryNotificationStore();
        var handler = new ArchiveHandler(store);
        var data = Sample(dedupKey: "backup-2026-04-28");
        var envelope = EnvelopeFor(data);

        var result = await handler.HandleAsync(envelope);

        var created = Assert.IsType<ArchiveResult.Created>(result);
        var expectedId = DedupKeyHasher.Hash(data.Source, "backup-2026-04-28");
        Assert.Equal(expectedId, created.Id);
        Assert.NotEqual(envelope.Id, created.Id);  // not the UUID
    }

    [Fact]
    public async Task Same_dedup_key_collides_and_returns_already_exists()
    {
        var store = new InMemoryNotificationStore();
        var handler = new ArchiveHandler(store);
        var data = Sample(dedupKey: "k");

        var first = await handler.HandleAsync(EnvelopeFor(data));
        var second = await handler.HandleAsync(EnvelopeFor(data));  // different envelope id, same dedup key

        Assert.IsType<ArchiveResult.Created>(first);
        var exists = Assert.IsType<ArchiveResult.AlreadyExists>(second);
        Assert.Equal(((ArchiveResult.Created)first).Id, exists.Id);
        Assert.Single(store.Items);
        Assert.Equal(2, store.CreateAttempts);
        Assert.Equal(1, store.Conflicts);
    }

    [Fact]
    public async Task Different_dedup_keys_create_distinct_documents()
    {
        var store = new InMemoryNotificationStore();
        var handler = new ArchiveHandler(store);

        await handler.HandleAsync(EnvelopeFor(Sample(dedupKey: "k1")));
        await handler.HandleAsync(EnvelopeFor(Sample(dedupKey: "k2")));

        Assert.Equal(2, store.Items.Count);
    }

    [Fact]
    public async Task Document_preserves_all_envelope_data_fields()
    {
        var store = new InMemoryNotificationStore();
        var handler = new ArchiveHandler(store);
        var data = new NotifyCreatedV1
        {
            Source = "home-pipeline",
            Title = "T",
            Body = "B",
            Type = "alert",
            Priority = Priority.High,
            Tags = new[] { "a", "b" },
            Deeplink = "https://example.com",
        };
        var envelope = EnvelopeFor(data);

        await handler.HandleAsync(envelope);

        var doc = store.Items.Values.Single();
        Assert.Equal("home-pipeline", doc.Source);
        Assert.Equal("T", doc.Title);
        Assert.Equal("B", doc.Body);
        Assert.Equal("alert", doc.Type);
        Assert.Equal(Priority.High, doc.Priority);
        Assert.Equal(new[] { "a", "b" }, doc.Tags);
        Assert.Equal("https://example.com", doc.Deeplink);
        Assert.Null(doc.ReadAt);
    }

    [Fact]
    public async Task Falls_back_to_envelope_time_when_data_timestamp_is_null()
    {
        var store = new InMemoryNotificationStore();
        var handler = new ArchiveHandler(store);
        var time = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var envelope = EnvelopeFor(Sample(), time: time);

        await handler.HandleAsync(envelope);

        var doc = store.Items.Values.Single();
        Assert.Equal(time, doc.Timestamp);
    }
}
