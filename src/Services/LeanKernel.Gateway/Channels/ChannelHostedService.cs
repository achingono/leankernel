using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Gateway.Channels;

/// <summary>
/// A hosted service that manages the lifecycle of all configured channel adapters.
/// Subscribes to message events and routes them through the <see cref="IChannelRouter"/>.
/// </summary>
public sealed class ChannelHostedService : IHostedService
{
    private readonly IReadOnlyList<IChannel> _channels;
    private readonly IChannelRouter _router;
    private readonly ChannelsSettings _config;
    private readonly ILogger<ChannelHostedService> _logger;
    private readonly Dictionary<IChannel, Func<ChannelMessage, Task>> _subscriptions = [];
    private bool _started;

    public ChannelHostedService(
        IEnumerable<IChannel> channels,
        IChannelRouter router,
        IOptions<ChannelsSettings> config,
        ILogger<ChannelHostedService> logger)
    {
        _channels = (channels ?? throw new ArgumentNullException(nameof(channels))).ToArray();
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("Channel hosted service is disabled via configuration");
            return;
        }

        if (_started)
        {
            return;
        }

        foreach (var channel in _channels)
        {
            if (!_subscriptions.ContainsKey(channel))
            {
                Func<ChannelMessage, Task> handler = message => HandleMessageAsync(channel, message);
                _subscriptions[channel] = handler;
                channel.MessageReceived += handler;
            }

            await channel.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        _started = true;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Key.MessageReceived -= subscription.Value;
        }

        _subscriptions.Clear();

        foreach (var channel in _channels)
        {
            await channel.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        _started = false;
    }

    private async Task HandleMessageAsync(IChannel channel, ChannelMessage message)
    {
        try
        {
            await _router.RouteInboundAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to route inbound channel message for {ChannelId}",
                channel.ChannelId);
        }
    }
}
