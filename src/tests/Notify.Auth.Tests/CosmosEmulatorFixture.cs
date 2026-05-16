using Microsoft.Azure.Cosmos;
using Notify.Shared.Json;

namespace Notify.Auth.Tests;

// Per-test-class Cosmos emulator fixture for the allowlist repository tests.
// Mirrors the pattern in Notify.Archive.Tests / Notify.Inbox.Tests — separate
// instance so the Auth project doesn't pull in archive helpers it doesn't
// need.
public sealed class CosmosEmulatorFixture : IAsyncLifetime
{
    private const string DefaultEndpoint = "https://localhost:8081";
    private const string EmulatorKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    public CosmosClient Client { get; private set; } = null!;
    public Container AllowedUsers { get; private set; } = null!;
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
            Id = "allowedUsers",
            PartitionKeyPath = "/id",
        });
        AllowedUsers = container.Container;
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
