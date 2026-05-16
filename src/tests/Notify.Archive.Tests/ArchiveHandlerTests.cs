using Notify.Functions.Archive;
using Notify.Shared;
using Notify.Shared.CloudEvents;
using Notify.Shared.Hashing;

namespace Notify.Functions.Archive.Tests;

public class ArchiveHandlerTests
{
    private const string UserA = "001234.aaaaaaaa";
    private const string UserB = "001234.bbbbbbbb";

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
    public async Task Without_dedup_key_doc_id_is_envelope_id_suffixed_with_user()
    {
        var sink = new InMemoryArchiveSink();
        var users = new InMemoryUserDirectory();
        users.UserIds.Add(UserA);
        var handler = new ArchiveHandler(sink, users);

        var envelope = NewEnvelope();
        var result = await handler.HandleAsync(envelope);

        Assert.Equal(1, result.SubscribedUsers);
        Assert.Equal(1, result.Created);
        var doc = Assert.Single(sink.Stored.Values);
        Assert.Equal($"{envelope.Id}:{UserA}", doc.Id);
        Assert.Equal(envelope.Id, doc.EnvelopeId);
        Assert.Equal(UserA, doc.UserId);
        Assert.Equal("home-pipeline", doc.Source);
    }

    [Fact]
    public async Task With_dedup_key_doc_id_is_hash_suffixed_with_user()
    {
        var sink = new InMemoryArchiveSink();
        var users = new InMemoryUserDirectory();
        users.UserIds.Add(UserA);
        var handler = new ArchiveHandler(sink, users);

        var envelope = NewEnvelope(dedup: "backup-2026-04-28");
        await handler.HandleAsync(envelope);

        var doc = Assert.Single(sink.Stored.Values);
        var expectedHash = DedupKeyHasher.Hash("home-pipeline", "backup-2026-04-28");
        Assert.Equal($"{expectedHash}:{UserA}", doc.Id);
        Assert.Equal(envelope.Id, doc.EnvelopeId);
        Assert.Equal(UserA, doc.UserId);
    }

    [Fact]
    public async Task Fans_out_one_document_per_subscribed_user()
    {
        var sink = new InMemoryArchiveSink();
        var users = new InMemoryUserDirectory();
        users.UserIds.AddRange(new[] { UserA, UserB });
        var handler = new ArchiveHandler(sink, users);

        var result = await handler.HandleAsync(NewEnvelope());

        Assert.Equal(2, result.SubscribedUsers);
        Assert.Equal(2, result.Created);
        Assert.Equal(2, sink.Stored.Count);
        Assert.Contains(sink.Stored.Values, d => d.UserId == UserA);
        Assert.Contains(sink.Stored.Values, d => d.UserId == UserB);
    }

    [Fact]
    public async Task Zero_users_results_in_zero_writes_but_no_error()
    {
        var sink = new InMemoryArchiveSink();
        var users = new InMemoryUserDirectory();   // empty
        var handler = new ArchiveHandler(sink, users);

        var result = await handler.HandleAsync(NewEnvelope());

        Assert.Equal(0, result.SubscribedUsers);
        Assert.Equal(0, result.Created);
        Assert.Empty(sink.Stored);
    }

    [Fact]
    public async Task Same_dedup_key_twice_per_user_collapses()
    {
        var sink = new InMemoryArchiveSink();
        var users = new InMemoryUserDirectory();
        users.UserIds.Add(UserA);
        var handler = new ArchiveHandler(sink, users);

        var first  = await handler.HandleAsync(NewEnvelope(dedup: "k"));
        var second = await handler.HandleAsync(NewEnvelope(dedup: "k"));

        Assert.Equal(1, first.Created);
        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Duplicates);
        Assert.Single(sink.Stored);
    }

    [Fact]
    public async Task Doc_carries_full_payload_plus_user_binding()
    {
        var sink = new InMemoryArchiveSink();
        var users = new InMemoryUserDirectory();
        users.UserIds.Add(UserA);
        var handler = new ArchiveHandler(sink, users);

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
        Assert.Equal(UserA, doc.UserId);
        Assert.Equal("alert", doc.Type);
        Assert.Equal(Priority.High, doc.Priority);
        Assert.Equal(new[] { "pi-01" }, doc.Tags);
        Assert.Equal("https://example/a", doc.Deeplink);
    }
}
