using Microsoft.Extensions.Hosting;
using LeanKernel.Host.Services.Channels;
using LeanKernel.Host.Services.Channels.Adapters;

namespace LeanKernel.Host.Services;

/// <summary>
/// Initializes channels and registers them with the registry on application startup.
/// </summary>
internal sealed class ChannelInitializationService : IHostedService
{
    private readonly ChannelRegistry _registry;
    private readonly SignalChannelAdapter _signalAdapter;
    private readonly DiscordChannelAdapter _discordAdapter;

    public ChannelInitializationService(
        ChannelRegistry registry,
        SignalChannelAdapter signalAdapter,
        DiscordChannelAdapter discordAdapter)
    {
        _registry = registry;
        _signalAdapter = signalAdapter;
        _discordAdapter = discordAdapter;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registry.RegisterChannel(_signalAdapter);
        _registry.RegisterChannel(_discordAdapter);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
