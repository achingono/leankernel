using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels;

/// <summary>
/// Extension methods for configuring channels in the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the LeanKernel channels to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="config">The channels configuration.</param>
    /// <returns>The service collection after adding channels.</returns>
    public static IServiceCollection AddLeanKernelChannels(this IServiceCollection services, ChannelsConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.AddSingleton<IOptions<ChannelsConfig>>(Options.Create(config));
        services.TryAddSingleton<IOptions<LeanKernelConfig>>(Options.Create(new LeanKernelConfig()));
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
