using Azure.Identity;
using Azure.Messaging.EventGrid;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.NotificationHubs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Notify.Functions.Admin;
using Notify.Functions.Archive;
using Notify.Functions.Auth;
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
    .ConfigureFunctionsWorkerDefaults(worker =>
    {
        // Two middlewares, both run on every HTTP request. AdminAuthMiddleware
        // short-circuits the request only if the path matches `/admin/`;
        // JwtAuthMiddleware short-circuits only when there's a Bearer header
        // on a non-admin path. They are mutually exclusive on the request
        // path, so order doesn't matter for correctness — putting Admin first
        // means /admin/* requests skip the Apple JWKS fetch on the hot path.
        worker.UseMiddleware<AdminAuthMiddleware>();
        worker.UseMiddleware<JwtAuthMiddleware>();
    })
    .ConfigureServices((ctx, services) =>
    {
        services.AddOptions<IngestionOptions>().Bind(ctx.Configuration);
        services.AddOptions<ArchiveOptions>().Bind(ctx.Configuration);
        services.AddOptions<DevicesOptions>().Bind(ctx.Configuration);
        services.AddOptions<PushOptions>().Bind(ctx.Configuration);
        services.AddOptions<InboxOptions>().Bind(ctx.Configuration);
        services.AddOptions<AuthOptions>().Bind(ctx.Configuration.GetSection("Auth"));
        services.AddOptions<AdminOptions>().Bind(ctx.Configuration.GetSection("Admin"));

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
        services.AddSingleton<IUserDirectory>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ArchiveOptions>>().Value;
            var cosmos = sp.GetRequiredService<CosmosClient>();
            return new CosmosUserDirectory(cosmos.GetContainer(opts.CosmosDatabase, opts.CosmosDevicesContainer));
        });
        services.AddSingleton<ArchiveHandler>();

        // ── Inbox ────────────────────────────────────────────────────────
        services.AddSingleton<IInboxQuery>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<InboxOptions>>().Value;
            var cosmos = sp.GetRequiredService<CosmosClient>();
            return new CosmosInboxQuery(cosmos.GetContainer(opts.CosmosDatabase, opts.CosmosNotificationsContainer));
        });
        services.AddSingleton<IInboxMutator>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<InboxOptions>>().Value;
            var cosmos = sp.GetRequiredService<CosmosClient>();
            return new CosmosInboxMutator(cosmos.GetContainer(opts.CosmosDatabase, opts.CosmosNotificationsContainer));
        });
        services.AddSingleton<InboxHandler>();
        services.AddSingleton<InboxMutationHandler>();

        // ── Notification Hubs (Devices + Push share the client) ─────────
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DevicesOptions>>().Value;
            return NotificationHubClient.CreateClientFromConnectionString(opts.NotificationHubConnectionString, opts.NotificationHubName);
        });

        // ── Devices ──────────────────────────────────────────────────────
        services.AddSingleton<INotificationHub, NotificationHubAdapter>();
        services.AddSingleton<IDeviceStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DevicesOptions>>().Value;
            var cosmos = sp.GetRequiredService<CosmosClient>();
            return new CosmosDeviceStore(cosmos.GetContainer(opts.CosmosDatabase, opts.CosmosDevicesContainer));
        });
        services.AddSingleton<RegisterHandler>();

        // ── Push ─────────────────────────────────────────────────────────
        services.AddSingleton<INotificationSender, NotificationHubSender>();
        services.AddSingleton<PushHandler>();

        // ── Auth (Sign in with Apple) ────────────────────────────────────
        // HttpClient + memory cache for the JWKS fetcher; AppleJwtValidator is
        // stateless and depends only on those. The middleware resolves the
        // validator per request via FunctionContext.InstanceServices.
        services.AddMemoryCache();
        services.AddHttpClient<IAppleJwksProvider, AppleJwksProvider>();
        services.AddSingleton<AppleJwtValidator>();
        // Session JWT minting + validation. Apple tokens are exchanged once
        // at /v1/auth/session; every other protected route accepts only the
        // session JWT, signed with the KV-backed `Auth__SessionSigningKey`.
        services.AddSingleton<SessionTokenIssuer>();
        services.AddSingleton<SessionTokenValidator>();
        services.AddSingleton<AuthFunctions>();
        // Allowlist gate. When `Auth__CosmosAllowedUsersContainer` is unset we
        // bind the no-op implementation so forks that haven't deployed the
        // bicep change yet still authenticate (any valid SiwA JWT goes
        // through). Once the setting is present, every authenticated request
        // gets a point-read against `allowedUsers/<sub>` with 60-s caching.
        services.AddSingleton<IAllowlistRepository>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AuthOptions>>().Value;
            if (string.IsNullOrWhiteSpace(opts.CosmosAllowedUsersContainer)
                || string.IsNullOrWhiteSpace(opts.CosmosDatabase))
                return new AlwaysApproveAllowlistRepository();
            var cosmos = sp.GetRequiredService<CosmosClient>();
            var cache = sp.GetRequiredService<IMemoryCache>();
            return new CosmosAllowlistRepository(
                cosmos.GetContainer(opts.CosmosDatabase, opts.CosmosAllowedUsersContainer),
                cache);
        });

        // ── Admin (Entra ID) ─────────────────────────────────────────────
        // Mirrors the Apple-side wiring: HttpClient + cached JWKS fetcher +
        // stateless validator. Handlers run against the same `allowedUsers`
        // container the iOS-side allowlist gate writes to.
        services.AddHttpClient<IEntraJwksProvider, EntraJwksProvider>();
        services.AddSingleton<EntraJwtValidator>();
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AuthOptions>>().Value;
            var cosmos = sp.GetRequiredService<CosmosClient>();
            return new AllowlistAdminHandler(
                cosmos.GetContainer(opts.CosmosDatabase, opts.CosmosAllowedUsersContainer));
        });
        services.AddSingleton(sp =>
        {
            // Projects admin handler reuses the existing IngestionOptions
            // pointer to the projects container + the same ApiKeyHasher the
            // Ingest path verifies against, so admin-minted keys round-trip
            // through Ingest without any second hash contract.
            var opts = sp.GetRequiredService<IOptions<IngestionOptions>>().Value;
            var cosmos = sp.GetRequiredService<CosmosClient>();
            var hasher = sp.GetRequiredService<ApiKeyHasher>();
            return new ProjectsAdminHandler(
                cosmos.GetContainer(opts.CosmosDatabase, opts.CosmosProjectsContainer),
                hasher);
        });
    })
    .Build();

host.Run();
