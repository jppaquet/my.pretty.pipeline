using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Notify.IngestionApi;
using Notify.IngestionApi.Ingestion;
using Notify.Shared.Cosmos;
using Notify.Shared.Hashing;
using Notify.Shared.Json;

namespace Notify.IngestionApi.Tests;

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

    private static Stream BodyOf(object obj)
        => new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj, NotifyJson.Options)));

    private static object ValidBody(string source = "home-pipeline") => new
    {
        source,
        title = "Backup failed",
        body = "rsync exited 12 on host pi-01",
        priority = "high",
    };

    [Fact]
    public async Task Missing_api_key_returns_unauthorized()
    {
        var (handler, _, _) = NewHandler();
        var result = await handler.HandleAsync(apiKey: null, BodyOf(ValidBody()), contentLength: null);
        Assert.IsType<IngestResult.Unauthorized>(result);
    }

    [Fact]
    public async Task Empty_api_key_returns_unauthorized()
    {
        var (handler, _, _) = NewHandler();
        var result = await handler.HandleAsync(apiKey: "   ", BodyOf(ValidBody()), contentLength: null);
        Assert.IsType<IngestResult.Unauthorized>(result);
    }

    [Fact]
    public async Task Oversized_payload_returns_413_without_parsing_body()
    {
        var (handler, _, _) = NewHandler();
        var result = await handler.HandleAsync("npk_x", Stream.Null, contentLength: IngestionApiOptions.MaxRequestBodyBytes + 1);
        Assert.IsType<IngestResult.PayloadTooLarge>(result);
    }

    [Fact]
    public async Task Malformed_json_returns_bad_request()
    {
        var (handler, _, _) = NewHandler();
        var bad = new MemoryStream(Encoding.UTF8.GetBytes("{not json"));
        var result = await handler.HandleAsync("npk_x", bad, contentLength: null);
        var br = Assert.IsType<IngestResult.BadRequest>(result);
        Assert.Contains(br.Failures, f => f.Field == "body");
    }

    [Fact]
    public async Task Validation_failure_returns_bad_request_before_cosmos_lookup()
    {
        var (handler, projects, _) = NewHandler();
        var body = new { source = "", title = "", body = "" };
        var result = await handler.HandleAsync("npk_x", BodyOf(body), contentLength: null);
        Assert.IsType<IngestResult.BadRequest>(result);
        Assert.Empty(projects.Projects);
    }

    [Fact]
    public async Task Unknown_project_returns_unauthorized()
    {
        var (handler, _, _) = NewHandler();
        var result = await handler.HandleAsync("npk_x", BodyOf(ValidBody("nope")), contentLength: null);
        Assert.IsType<IngestResult.Unauthorized>(result);
    }

    [Fact]
    public async Task Inactive_project_returns_unauthorized()
    {
        var (handler, projects, _) = NewHandler();
        SeedProject(projects, "home-pipeline", "npk_correct", active: false);
        var result = await handler.HandleAsync("npk_correct", BodyOf(ValidBody()), contentLength: null);
        Assert.IsType<IngestResult.Unauthorized>(result);
    }

    [Fact]
    public async Task Wrong_key_returns_unauthorized()
    {
        var (handler, projects, _) = NewHandler();
        SeedProject(projects, "home-pipeline", "npk_correct");
        var result = await handler.HandleAsync("npk_wrong", BodyOf(ValidBody()), contentLength: null);
        Assert.IsType<IngestResult.Unauthorized>(result);
    }

    [Fact]
    public async Task Happy_path_returns_202_and_publishes_envelope()
    {
        var (handler, projects, publisher) = NewHandler();
        SeedProject(projects, "home-pipeline", "npk_correct");

        var result = await handler.HandleAsync("npk_correct", BodyOf(ValidBody()), contentLength: null);

        var accepted = Assert.IsType<IngestResult.Accepted>(result);
        Assert.False(string.IsNullOrEmpty(accepted.Id));

        var env = Assert.Single(publisher.Published);
        Assert.Equal("notify.created.v1", env.Type);
        Assert.Equal("urn:notify:home-pipeline", env.Source);
        Assert.Equal(accepted.Id, env.Id);
        Assert.Equal("home-pipeline", env.Data.Source);
        Assert.Equal("Backup failed", env.Data.Title);
    }

    [Fact]
    public async Task Server_overrides_source_to_match_authenticated_project()
    {
        var (handler, projects, publisher) = NewHandler();
        SeedProject(projects, "home-pipeline", "npk_correct");

        // Producer lies and submits a different source than its API key's project.
        var body = new { source = "spoofed", title = "t", body = "b" };

        var result = await handler.HandleAsync("npk_correct", BodyOf(body), contentLength: null);

        // Cosmos lookup uses the body's source, so this returns Unauthorized
        // (no project doc with id="spoofed").
        Assert.IsType<IngestResult.Unauthorized>(result);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Server_fills_id_and_timestamp()
    {
        var (handler, projects, publisher) = NewHandler();
        SeedProject(projects, "home-pipeline", "npk_correct");

        var before = DateTimeOffset.UtcNow;
        var result = await handler.HandleAsync("npk_correct", BodyOf(ValidBody()), contentLength: null);
        var after = DateTimeOffset.UtcNow;

        var accepted = Assert.IsType<IngestResult.Accepted>(result);
        var env = publisher.Published.Single();

        Assert.True(Guid.TryParse(accepted.Id, out _));
        Assert.NotNull(env.Data.Timestamp);
        Assert.InRange(env.Data.Timestamp!.Value, before.AddSeconds(-1), after.AddSeconds(1));
    }
}
