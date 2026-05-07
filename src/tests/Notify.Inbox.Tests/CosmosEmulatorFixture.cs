using Microsoft.Azure.Cosmos;
using Notify.Shared.Json;

namespace Notify.Functions.Inbox.Tests;

// Spins up a per-test-class Cosmos database+container against the local
// emulator (CI: docker-service container; dev: `docker compose up -d`).
// Mirror of the fixture in Notify.Archive.Tests — duplicated rather than
// shared so each test project stays self-contained.
public sealed class CosmosEmulatorFixture : IAsyncLifetime
{
    private const string DefaultEndpoint = "https://localhost:8081";
    private const string EmulatorKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    public CosmosClient Client { get; private set; } = null!;
    public Container Notifications { get; private set; } = null!;
    private string _databaseId = "";

    public async Task InitializeAsync()
    {
        var endpoint = Environment.GetEnvironmentVariable("COSMOS_EMULATOR_ENDPOINT") ?? DefaultEndpoint;
        var key = Environment.GetEnvironmentVariable("COSMOS_EMULATOR_KEY") ?? EmulatorKey;

        Client = new CosmosClient(endpoint, key, new CosmosClientOptions
        {
            UseSystemTextJsonSerializerWithOptions = NotifyJson.Options,
            HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            }),
            ConnectionMode = ConnectionMode.Gateway,
        });

        _databaseId = $"notify-test-{Guid.NewGuid():N}";
        var db = await Client.CreateDatabaseAsync(_databaseId);
        var container = await db.Database.CreateContainerAsync(new ContainerProperties
        {
            Id = "notifications",
            PartitionKeyPath = "/source",
            DefaultTimeToLive = 60 * 60 * 24 * 90,
        });
        Notifications = container.Container;
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrEmpty(_databaseId))
        {
            try { await Client.GetDatabase(_databaseId).DeleteAsync(); } catch { /* best-effort */ }
        }
        Client.Dispose();
    }
}
