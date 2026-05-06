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
