using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels;

/// <summary>
/// A hosted service that manages the lifecycle and message routing for all configured channels.
/// </summary>
public sealed class ChannelHostedService(
    IEnumerable<IChannel> channels,
    IChannelRouter router,
    IOptions<ChannelsConfig> config,
    ILogger<ChannelHostedService> logger) : IHostedService
{
    private readonly IReadOnlyList<IChannel> _channels = (channels ?? throw new ArgumentNullException(nameof(channels))).ToArray();
    private readonly IChannelRouter _router = router ?? throw new ArgumentNullException(nameof(router));
    private readonly ChannelsConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
    private readonly ILogger<ChannelHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Dictionary<IChannel, Func<ChannelMessage, Task>> _subscriptions = [];
    private bool _started;

    /// <summary>
    /// Starts the hosted service, initializing and starting all configured channels.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync(CancellationToken ct)
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

            await channel.StartAsync(ct).ConfigureAwait(false);
        }

        _started = true;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct)
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Key.MessageReceived -= subscription.Value;
        }

        _subscriptions.Clear();

        foreach (var channel in _channels)
        {
            await channel.StopAsync(ct).ConfigureAwait(false);
        }

        _started = false;
    }

    /// <summary>
    /// Handles inbound messages from a channel and routes them through the router.
    /// </summary>
    /// <param name="channel">The source channel.</param>
    /// <param name="message">The message to route.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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
