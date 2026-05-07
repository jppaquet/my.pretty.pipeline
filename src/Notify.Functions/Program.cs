using Azure.Identity;
using Azure.Messaging.EventGrid;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.NotificationHubs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Notify.Functions.Archive;
using Notify.Functions.Devices;
using Notify.Functions.Inbox;
using Notify.Functions.Ingestion;
using Notify.Functions.Push;
using Notify.Shared.Hashing;
using Notify.Shared.Json;

// Single composition root for every Notify.* function. Each feature folder
// declares its own typed-options record bound from configuration; this
// Program.cs wires the shared infrastructure (CosmosClient, EventGrid,
// NotificationHubClient) once and the per-feature handlers on top.

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        services.AddOptions<IngestionOptions>().Bind(ctx.Configuration);
        services.AddOptions<ArchiveOptions>().Bind(ctx.Configuration);
        services.AddOptions<DevicesOptions>().Bind(ctx.Configuration);
        services.AddOptions<PushOptions>().Bind(ctx.Configuration);
        services.AddOptions<InboxOptions>().Bind(ctx.Configuration);

        // Single CosmosClient — both Ingestion (project lookup) and Archive
        // (notifications upsert) share it. Endpoint comes from IngestionOptions
        // because it lands first in CONFIG; ArchiveOptions has the same value
        // (source: COSMOS_ACCOUNT_ENDPOINT app setting bound to both records).
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<IngestionOptions>>().Value;
            return new CosmosClient(opts.CosmosAccountEndpoint, new DefaultAzureCredential(), new CosmosClientOptions
            {
                UseSystemTextJsonSerializerWithOptions = NotifyJson.Options,
            });
        });

        // ── Ingestion ────────────────────────────────────────────────────
        services.AddSingleton<IEventPublisher>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<IngestionOptions>>().Value;
            var client = new EventGridPublisherClient(new Uri(opts.EventGridTopicEndpoint), new DefaultAzureCredential());
            return new EventGridEventPublisher(client);
        });
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<IngestionOptions>>().Value;
            return new ApiKeyHasher(Convert.FromBase64String(opts.ApiKeyPepperBase64));
        });
        services.AddSingleton<IProjectLookup>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<IngestionOptions>>().Value;
            var cosmos = sp.GetRequiredService<CosmosClient>();
            return new CosmosProjectLookup(cosmos.GetContainer(opts.CosmosDatabase, opts.CosmosProjectsContainer));
        });
        services.AddSingleton<IngestHandler>();

        // ── Archive ──────────────────────────────────────────────────────
        services.AddSingleton<IArchiveSink>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ArchiveOptions>>().Value;
            var cosmos = sp.GetRequiredService<CosmosClient>();
            return new CosmosArchiveSink(cosmos.GetContainer(opts.CosmosDatabase, opts.CosmosNotificationsContainer));
        });
        services.AddSingleton<ArchiveHandler>();

        // ── Inbox ────────────────────────────────────────────────────────
        services.AddSingleton<IInboxQuery>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<InboxOptions>>().Value;
            var cosmos = sp.GetRequiredService<CosmosClient>();
            return new CosmosInboxQuery(cosmos.GetContainer(opts.CosmosDatabase, opts.CosmosNotificationsContainer));
        });
        services.AddSingleton<InboxHandler>();

        // ── Notification Hubs (Devices + Push share the client) ─────────
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DevicesOptions>>().Value;
            return NotificationHubClient.CreateClientFromConnectionString(opts.NotificationHubConnectionString, opts.NotificationHubName);
        });

        // ── Devices ──────────────────────────────────────────────────────
        services.AddSingleton<INotificationHub, NotificationHubAdapter>();
        services.AddSingleton<RegisterHandler>();

        // ── Push ─────────────────────────────────────────────────────────
        services.AddSingleton<INotificationSender, NotificationHubSender>();
        services.AddSingleton<PushHandler>();
    })
    .Build();

host.Run();
