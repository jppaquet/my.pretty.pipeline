using Microsoft.Azure.NotificationHubs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Notify.PushDelivery;
using Notify.PushDelivery.Push;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        services.AddOptions<PushDeliveryOptions>().Bind(ctx.Configuration);

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<PushDeliveryOptions>>().Value;
            return NotificationHubClient.CreateClientFromConnectionString(opts.NotificationHubConnectionString, opts.NotificationHubName);
        });

        services.AddSingleton<INotificationSender, NotificationHubSender>();
        services.AddSingleton<PushHandler>();
    })
    .Build();

host.Run();
