using Microsoft.Azure.Cosmos;
using Notify.Shared.Json;

namespace Notify.Auth.Tests;

// Collection definition that shares ONE CosmosEmulatorFixture across every
// integration test class in this assembly. Without it, xUnit creates one
// fixture per [IClassFixture<>] consumer and runs the classes in parallel —
// two simultaneous CreateDatabase + CreateContainer calls against the
// emulator trip an internal-server-error (~87ms, 500). The collection both
// shares the resource and forces the tagged classes to run in the same
// (serial) test collection.
[CollectionDefinition("Cosmos")]
public sealed class CosmosCollection : ICollectionFixture<CosmosEmulatorFixture> { }

// Shared Cosmos emulator fixture for every integration test class in this
// assembly. Mirrors the pattern in Notify.Archive.Tests / Notify.Inbox.Tests
// (which currently have only one consumer each, so IClassFixture is fine
// there) — separate file so the Auth project doesn't pull in archive
// helpers it doesn't need.
public sealed class CosmosEmulatorFixture : IAsyncLifetime
{
    private const string DefaultEndpoint = "https://localhost:8081";
    private const string EmulatorKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    public CosmosClient Client { get; private set; } = null!;
    public Container AllowedUsers { get; private set; } = null!;
    public Container Projects { get; private set; } = null!;
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

        var allowed = await db.Database.CreateContainerAsync(new ContainerProperties
        {
            Id = "allowedUsers",
            PartitionKeyPath = "/id",
        });
        AllowedUsers = allowed.Container;

        // Shared across every projects-admin test method in this assembly.
        // We deliberately don't create one-container-per-test: with the
        // emulator's `AZURE_COSMOS_EMULATOR_PARTITION_COUNT=10` ceiling,
        // 10 ProjectsAdminHandlerTests × 1 container each would exhaust
        // the pool and trip 503s. Tests use unique projectIds per mint
        // so isolation is preserved even with shared storage.
        var projects = await db.Database.CreateContainerAsync(new ContainerProperties
        {
            Id = "projects",
            PartitionKeyPath = "/projectId",
        });
        Projects = projects.Container;
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
