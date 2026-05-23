using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLeanKernelChannels(this IServiceCollection services, ChannelsConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.AddSingleton<IOptions<ChannelsConfig>>(Options.Create(config));
        services.AddSingleton<IChannelRouter, ChannelRouter>();
        services.AddSingleton<ChannelAuthenticator>();

        if (config.Signal.Enabled)
        {
            services.AddSingleton<IChannel, SignalChannel>();
            services.AddHttpClient("signal-daemon", client =>
            {
                client.BaseAddress = new Uri(config.Signal.DaemonUrl);
            });
        }

        services.AddHostedService<ChannelHostedService>();
        return services;
    }
}
