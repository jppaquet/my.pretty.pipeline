using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Notify.Archive;
using Notify.Archive.Archiving;
using Notify.Shared.Json;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        services.AddOptions<ArchiveOptions>().Bind(ctx.Configuration);

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ArchiveOptions>>().Value;
            return new CosmosClient(opts.CosmosAccountEndpoint, new DefaultAzureCredential(), new CosmosClientOptions
            {
                UseSystemTextJsonSerializerWithOptions = NotifyJson.Options,
            });
        });

        services.AddSingleton<IArchiveSink>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ArchiveOptions>>().Value;
            var cosmos = sp.GetRequiredService<CosmosClient>();
            return new CosmosArchiveSink(cosmos.GetContainer(opts.CosmosDatabase, opts.CosmosNotificationsContainer));
        });

        services.AddSingleton<ArchiveHandler>();
    })
    .Build();

host.Run();
