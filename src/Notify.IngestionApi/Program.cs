using Azure.Identity;
using Azure.Messaging.EventGrid;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Notify.IngestionApi;
using Notify.IngestionApi.Ingestion;
using Notify.Shared.Hashing;
using Notify.Shared.Json;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        services.AddOptions<IngestionApiOptions>().Bind(ctx.Configuration);

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<IngestionApiOptions>>().Value;
            return new CosmosClient(opts.CosmosAccountEndpoint, new DefaultAzureCredential(), new CosmosClientOptions
            {
                UseSystemTextJsonSerializerWithOptions = NotifyJson.Options,
            });
        });

        services.AddSingleton<IEventPublisher>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<IngestionApiOptions>>().Value;
            var client = new EventGridPublisherClient(new Uri(opts.EventGridTopicEndpoint), new DefaultAzureCredential());
            return new EventGridEventPublisher(client);
        });

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<IngestionApiOptions>>().Value;
            return new ApiKeyHasher(Convert.FromBase64String(opts.ApiKeyPepperBase64));
        });

        services.AddSingleton<IProjectLookup>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<IngestionApiOptions>>().Value;
            var cosmos = sp.GetRequiredService<CosmosClient>();
            return new CosmosProjectLookup(cosmos.GetContainer(opts.CosmosDatabase, opts.CosmosProjectsContainer));
        });

        services.AddSingleton<IngestHandler>();
    })
    .Build();

host.Run();
