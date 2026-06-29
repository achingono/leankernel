using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels;

/// <summary>
/// Routes messages received from channels to the agent runtime.
/// </summary>
public sealed class ChannelRouter : IChannelRouter
{
    private readonly IAgentRuntime _runtime;
    private readonly ChannelAuthenticator _authenticator;
    private readonly ChannelsConfig _config;
    private readonly ILogger<ChannelRouter> _logger;
    private readonly IReadOnlyDictionary<string, IChannel> _channels;

    /// <summary>
    /// Initializes a new instance of the <see cref:ChannelRouter/> class.
    /// </summary>
    /// <param name="runtime">The agent runtime.</param>
    /// <param name="authenticator">The channel authenticator.</param>
    /// <param name="channels">The collection of available channels.</param>
    /// <param name="config">The channel configuration.</param>
    /// <param name="logger">The logger.</param>
    public ChannelRouter(
        IAgentRuntime runtime,
        ChannelAuthenticator authenticator,
        IEnumerable<IChannel> channels,
        IOptions<ChannelsConfig> config,
        ILogger<ChannelRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(channels);

        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var groupedChannels = channels
            .GroupBy(channel => channel.ChannelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var duplicate in groupedChannels.Where(group => group.Count() > 1))
        {
            _logger.LogWarning("Multiple channels were registered for {ChannelId}; the first registration will be used", duplicate.Key);
        }

        _channels = groupedChannels.ToDictionary(
            group => group.Key,
            group => group.First(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Routes an inbound channel message to the agent runtime.
    /// </summary>
    /// <param name="message">The message to route.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RouteInboundAsync(ChannelMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_config.Enabled)
        {
            _logger.LogDebug("Skipping inbound channel message because channels are disabled");
            return;
        }

        if (!_channels.TryGetValue(message.ChannelId, out var channel))
        {
            _logger.LogWarning(
                "Unable to route channel message for {ChannelId} from {SenderId}: no channel adapter is registered",
                message.ChannelId,
                message.SenderId);
            return;
        }

        var authorization = _authenticator.Authorize(message);
        if (!authorization.IsAuthorized)
        {
            _logger.LogWarning(
                "Rejected inbound channel message for {ChannelId} from {SenderId}: {Reason}",
                message.ChannelId,
                message.SenderId,
                authorization.Reason);
            return;
        }

        var runtimeMessage = new LeanKernelMessage
        {
            Content = message.Content,
            SenderId = message.SenderId,
            ChannelId = message.ChannelId,
            Timestamp = message.Timestamp,
            Attachments = message.Attachments
        };

        _logger.LogInformation(
            "Routing inbound channel message for {ChannelId} from {SenderId}",
            message.ChannelId,
            message.SenderId);

        await TrySignalTypingAsync(channel, message.SenderId, stop: false, ct).ConfigureAwait(false);

        try
        {
            var response = await _runtime.RunTurnAsync(runtimeMessage, ct).ConfigureAwait(false);
            await channel.SendAsync(message.SenderId, response, ct).ConfigureAwait(false);
        }
        finally
        {
            await TrySignalTypingAsync(channel, message.SenderId, stop: true, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Delivered channel response for {ChannelId} to {SenderId}",
            message.ChannelId,
            message.SenderId);
    }

    /// <summary>
    /// Updates the typing indicator for a channel.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="recipientId">The recipient identifier.</param>
    /// <param name="stop">Whether to stop the typing indicator.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task TrySignalTypingAsync(IChannel channel, string recipientId, bool stop, CancellationToken ct)
    {
        try
        {
            if (stop)
            {
                await channel.StopTypingAsync(recipientId, ct).ConfigureAwait(false);
            }
            else
            {
                await channel.StartTypingAsync(recipientId, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Typing indicator update failed for {ChannelId} to {RecipientId} (stop={Stop})",
                channel.ChannelId,
                recipientId,
                stop);
        }
    }
}
