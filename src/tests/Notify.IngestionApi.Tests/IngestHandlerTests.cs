using System.Security.Cryptography;
using Notify.Functions.Ingestion;
using Notify.Shared.CloudEvents;
using Notify.Shared.Cosmos;
using Notify.Shared.Hashing;

namespace Notify.Functions.Ingestion.Tests;

public class IngestHandlerTests
{
    private static readonly byte[] Pepper = RandomNumberGenerator.GetBytes(32);
    private static readonly ApiKeyHasher Hasher = new(Pepper);

    private static (IngestHandler handler, InMemoryProjectLookup projects, RecordingPublisher publisher) NewHandler()
    {
        var projects = new InMemoryProjectLookup();
        var publisher = new RecordingPublisher();
        var handler = new IngestHandler(projects, publisher, Hasher);
        return (handler, projects, publisher);
    }

    private static void SeedProject(InMemoryProjectLookup lookup, string projectId, string apiKey, bool active = true)
    {
        var salt = ApiKeyHasher.NewSalt();
        var hash = Hasher.Hash(apiKey, salt);
        lookup.Projects[projectId] = new ProjectDocument
        {
            Id = projectId,
            ProjectId = projectId,
            DisplayName = projectId,
            SaltBase64 = Convert.ToBase64String(salt),
            KeyHashBase64 = Convert.ToBase64String(hash),
            Active = active,
        };
    }

    private static IngestInput Input(string source = "home-pipeline", string title = "Backup failed",
        string body = "rsync exited 12 on host pi-01", DateTimeOffset? time = null)
        => new(source, time, new NotifyCreatedV1 { Title = title, Body = body });

    [Fact]
    public async Task Missing_api_key_returns_unauthorized()
    {
        var (handler, _, _) = NewHandler();
        var result = await handler.HandleAsync(apiKey: null, new[] { Input() }, isBatch: false);
        Assert.IsType<IngestResult.Unauthorized>(result);
    }

    [Fact]
    public async Task Empty_api_key_returns_unauthorized()
    {
        var (handler, _, _) = NewHandler();
        var result = await handler.HandleAsync(apiKey: "   ", new[] { Input() }, isBatch: false);
        Assert.IsType<IngestResult.Unauthorized>(result);
    }

    [Fact]
    public async Task Empty_events_returns_bad_request()
    {
        var (handler, _, _) = NewHandler();
        var result = await handler.HandleAsync("npk_x", Array.Empty<IngestInput>(), isBatch: true);
        var br = Assert.IsType<IngestResult.BadRequest>(result);
        Assert.Contains(br.Failures, f => f.Field == "events");
    }

    [Fact]
    public async Task Batch_over_max_size_returns_bad_request()
    {
        var (handler, projects, publisher) = NewHandler();
        SeedProject(projects, "home-pipeline", "npk_correct");
        var events = Enumerable.Range(0, IngestionOptions.MaxBatchSize + 1).Select(_ => Input()).ToArray();

        var result = await handler.HandleAsync("npk_correct", events, isBatch: true);

        Assert.IsType<IngestResult.BadRequest>(result);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Validation_failure_returns_bad_request_before_cosmos_lookup()
    {
        var (handler, projects, _) = NewHandler();
        // Empty title/body fails NotifyCreatedV1Validator. The handler runs
        // validation per-event AFTER project lookup, so the project must exist
        // or we'd get Unauthorized first. Seed it.
        SeedProject(projects, "home-pipeline", "npk_correct");
        var bad = new IngestInput("home-pipeline", null, new NotifyCreatedV1 { Title = "", Body = "" });
        var result = await handler.HandleAsync("npk_correct", new[] { bad }, isBatch: false);
        var br = Assert.IsType<IngestResult.BadRequest>(result);
        Assert.Contains(br.Failures, f => f.Field == "title");
        Assert.Contains(br.Failures, f => f.Field == "body");
    }

    [Fact]
    public async Task Unknown_project_returns_unauthorized()
    {
        var (handler, _, _) = NewHandler();
        var result = await handler.HandleAsync("npk_x", new[] { Input("nope") }, isBatch: false);
        Assert.IsType<IngestResult.Unauthorized>(result);
    }

    [Fact]
    public async Task Inactive_project_returns_unauthorized()
    {
        var (handler, projects, _) = NewHandler();
        SeedProject(projects, "home-pipeline", "npk_correct", active: false);
        var result = await handler.HandleAsync("npk_correct", new[] { Input() }, isBatch: false);
        Assert.IsType<IngestResult.Unauthorized>(result);
    }

    [Fact]
    public async Task Wrong_key_returns_unauthorized()
    {
        var (handler, projects, _) = NewHandler();
        SeedProject(projects, "home-pipeline", "npk_correct");
        var result = await handler.HandleAsync("npk_wrong", new[] { Input() }, isBatch: false);
        Assert.IsType<IngestResult.Unauthorized>(result);
    }

    [Fact]
    public async Task Happy_path_single_returns_accepted_and_publishes_envelope()
    {
        var (handler, projects, publisher) = NewHandler();
        SeedProject(projects, "home-pipeline", "npk_correct");

        var result = await handler.HandleAsync("npk_correct", new[] { Input() }, isBatch: false);

        var accepted = Assert.IsType<IngestResult.Accepted>(result);
        Assert.False(string.IsNullOrEmpty(accepted.Id));

        var env = Assert.Single(publisher.Published);
        Assert.Equal("notify.created.v1", env.Type);
        Assert.Equal("urn:notify:home-pipeline", env.Source);
        Assert.Equal(accepted.Id, env.Id);
        Assert.Equal("home-pipeline", env.Data.Source);
        Assert.Equal("Backup failed", env.Data.Title);
        Assert.Empty(publisher.Batches);
    }

    [Fact]
    public async Task Server_overrides_data_source_to_match_authenticated_project()
    {
        var (handler, projects, publisher) = NewHandler();
        SeedProject(projects, "home-pipeline", "npk_correct");

        // Producer puts a misleading source in `data` — server ignores it and
        // locks `data.Source` to the project id resolved from `ce.source`.
        var input = new IngestInput("home-pipeline", null, new NotifyCreatedV1
        {
            Source = "spoofed",
            Title = "t",
            Body = "b",
        });

        var result = await handler.HandleAsync("npk_correct", new[] { input }, isBatch: false);

        Assert.IsType<IngestResult.Accepted>(result);
        var env = Assert.Single(publisher.Published);
        Assert.Equal("home-pipeline", env.Data.Source);
    }

    [Fact]
    public async Task Server_fills_id_and_timestamp_when_ce_time_absent()
    {
        var (handler, projects, publisher) = NewHandler();
        SeedProject(projects, "home-pipeline", "npk_correct");

        var before = DateTimeOffset.UtcNow;
        var result = await handler.HandleAsync("npk_correct", new[] { Input(time: null) }, isBatch: false);
        var after = DateTimeOffset.UtcNow;

        var accepted = Assert.IsType<IngestResult.Accepted>(result);
        var env = publisher.Published.Single();

        Assert.True(Guid.TryParse(accepted.Id, out _));
        Assert.NotNull(env.Data.Timestamp);
        Assert.InRange(env.Data.Timestamp!.Value, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public async Task Server_preserves_ce_time_when_provided()
    {
        var (handler, projects, publisher) = NewHandler();
        SeedProject(projects, "home-pipeline", "npk_correct");

        var producerTime = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var result = await handler.HandleAsync("npk_correct", new[] { Input(time: producerTime) }, isBatch: false);

        Assert.IsType<IngestResult.Accepted>(result);
        var env = publisher.Published.Single();
        Assert.Equal(producerTime, env.Data.Timestamp);
        Assert.Equal(producerTime, env.Time);
    }

    [Fact]
    public async Task Batch_mixed_sources_returns_bad_request()
    {
        var (handler, projects, publisher) = NewHandler();
        SeedProject(projects, "home-pipeline", "npk_correct");

        var events = new[] { Input("home-pipeline"), Input("other-project") };
        var result = await handler.HandleAsync("npk_correct", events, isBatch: true);

        var br = Assert.IsType<IngestResult.BadRequest>(result);
        Assert.Contains(br.Failures, f => f.Field.StartsWith("events["));
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Batch_happy_path_returns_accepted_batch_and_publishes_once()
    {
        var (handler, projects, publisher) = NewHandler();
        SeedProject(projects, "home-pipeline", "npk_correct");

        var events = new[]
        {
            Input(title: "a", body: "aa"),
            Input(title: "b", body: "bb"),
            Input(title: "c", body: "cc"),
        };

        var result = await handler.HandleAsync("npk_correct", events, isBatch: true);

        var ab = Assert.IsType<IngestResult.AcceptedBatch>(result);
        Assert.Equal(3, ab.Ids.Count);
        Assert.Equal(3, publisher.Published.Count);
        // Single SendEventsAsync call -> single batch recorded
        var batch = Assert.Single(publisher.Batches);
        Assert.Equal(3, batch.Count);
        Assert.Equal(new[] { "a", "b", "c" }, publisher.Published.Select(e => e.Data.Title));
        Assert.All(publisher.Published, env => Assert.Equal("home-pipeline", env.Data.Source));
    }

    [Fact]
    public async Task Batch_validation_failure_at_index_aborts_whole_batch()
    {
        var (handler, projects, publisher) = NewHandler();
        SeedProject(projects, "home-pipeline", "npk_correct");

        var events = new[]
        {
            Input(title: "ok", body: "ok"),
            new IngestInput("home-pipeline", null, new NotifyCreatedV1 { Title = "", Body = "" }),
            Input(title: "also ok", body: "ok"),
        };

        var result = await handler.HandleAsync("npk_correct", events, isBatch: true);

        var br = Assert.IsType<IngestResult.BadRequest>(result);
        // Both title + body failures, namespaced to events[1].
        Assert.Contains(br.Failures, f => f.Field == "events[1].title");
        Assert.Contains(br.Failures, f => f.Field == "events[1].body");
        Assert.Empty(publisher.Published);
        Assert.Empty(publisher.Batches);
    }
}
