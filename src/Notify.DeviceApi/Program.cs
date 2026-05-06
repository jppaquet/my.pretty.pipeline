using Microsoft.Azure.NotificationHubs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Notify.DeviceApi;
using Notify.DeviceApi.Devices;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        services.AddOptions<DeviceApiOptions>().Bind(ctx.Configuration);

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DeviceApiOptions>>().Value;
            return NotificationHubClient.CreateClientFromConnectionString(opts.NotificationHubConnectionString, opts.NotificationHubName);
        });

        services.AddSingleton<INotificationHub, NotificationHubAdapter>();
        services.AddSingleton<RegisterHandler>();
    })
    .Build();

host.Run();
