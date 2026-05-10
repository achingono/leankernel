using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Commander;

/// <summary>
/// Routes inbound messages from any IChannel to the Thinker,
/// and sends responses back through the originating channel.
/// </summary>
public sealed class ChannelRouter
{
    private readonly IThinkerService _thinker;
    private readonly IReadOnlyList<IChannel> _channels;
    private readonly ILogger<ChannelRouter> _logger;

    public ChannelRouter(
        IThinkerService thinker,
        IEnumerable<IChannel> channels,
        ILogger<ChannelRouter> logger)
    {
        _thinker = thinker;
        _channels = channels.ToList();
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        foreach (var channel in _channels)
        {
            channel.OnMessageReceived += HandleMessageAsync;
            await channel.StartAsync(ct);
            _logger.LogInformation("Channel started: {ChannelId}", channel.ChannelId);
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        foreach (var channel in _channels)
        {
            await channel.StopAsync(ct);
            channel.OnMessageReceived -= HandleMessageAsync;
        }
    }

    /// <summary>
    /// Resolves a configured channel by stable identifier or display name.
    /// </summary>
    /// <param name="channelName">The channel identifier or display name.</param>
    /// <returns>The configured channel, or <see langword="null" /> when no usable channel is registered.</returns>
    public IChannel? GetChannel(string channelName)
    {
        if (string.IsNullOrWhiteSpace(channelName))
        {
            _logger.LogWarning("Attempted to get channel with null or empty name");
            return null;
        }

        var channel = _channels.FirstOrDefault(c =>
            string.Equals(c.ChannelId, channelName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));

        if (channel is null)
        {
            _logger.LogWarning("Channel '{ChannelName}' not registered", channelName);
            return null;
        }

        if (!channel.IsConfigured)
        {
            _logger.LogWarning("Channel '{ChannelName}' is not properly configured", channelName);
            return null;
        }

        return channel;
    }

    /// <summary>
    /// Delivers an outbound queued message through the named channel.
    /// </summary>
    /// <param name="channelName">The channel identifier or display name.</param>
    /// <param name="recipientId">The channel-specific recipient identifier.</param>
    /// <param name="content">The message content to deliver.</param>
    /// <param name="ct">A token used to cancel delivery.</param>
    /// <returns>The delivery result from the resolved channel.</returns>
    public async Task<ChannelDeliveryResult> DeliverAsync(
        string channelName,
        string recipientId,
        string content,
        CancellationToken ct = default)
    {
        var channel = GetChannel(channelName);
        if (channel is null)
        {
            return ChannelDeliveryResult.Failed(
                channelName,
                $"Channel '{channelName}' not configured",
                retryable: false);
        }

        return await channel.DeliverAsync(recipientId, content, ct);
    }

    private async Task HandleMessageAsync(LeanKernelMessage message, CancellationToken ct)
    {
        var channel = _channels.FirstOrDefault(c => c.ChannelId == message.ChannelId);
        if (channel is null)
        {
            _logger.LogWarning("No channel found for {ChannelId}", message.ChannelId);
            return;
        }

        if (!channel.IsAuthorizedSender(message.SenderId))
        {
            _logger.LogWarning("Rejected message from unauthorized sender {Sender} on {Channel}",
                message.SenderId, message.ChannelId);
            return;
        }

        _logger.LogInformation("Message from {Sender} on {Channel}: {Content}",
            message.SenderId, message.ChannelId, Truncate(message.Content, 80));

        IAsyncDisposable typingScope = NoopAsyncDisposable.Instance;
        try
        {
            if (channel is ITypingIndicatorChannel typingChannel)
            {
                try
                {
                    typingScope = await typingChannel.BeginTypingAsync(message.SenderId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to start typing indicator for {Channel}/{Recipient}",
                        message.ChannelId, message.SenderId);
                }
            }

            await using (typingScope)
            {
                var response = await _thinker.ProcessAsync(message, ct);
                await channel.SendAsync(message.SenderId, response, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId}", message.Id);
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "...";
}
