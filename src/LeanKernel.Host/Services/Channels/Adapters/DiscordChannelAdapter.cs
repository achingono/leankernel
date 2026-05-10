using Microsoft.Extensions.Logging;
using CommanderDiscordChannelAdapter = LeanKernel.Commander.Adapters.DiscordChannelAdapter;

namespace LeanKernel.Host.Services.Channels.Adapters;

/// <summary>
/// Compatibility adapter for the legacy Host channel registry.
/// </summary>
public sealed class DiscordChannelAdapter : IMessageChannel
{
    private readonly CommanderDiscordChannelAdapter _inner;

    public DiscordChannelAdapter(
        ILogger<DiscordChannelAdapter> logger,
        HttpClient httpClient,
        string? botToken,
        string? channelId)
    {
        _inner = new CommanderDiscordChannelAdapter(logger, httpClient, botToken, channelId);
    }

    public string Name => _inner.Name;

    public bool IsConfigured => _inner.IsConfigured;

    public async Task<ChannelDeliveryResult> DeliverAsync(
        string recipient,
        string content,
        CancellationToken ct = default)
    {
        var result = await _inner.DeliverAsync(recipient, content, ct);
        return result.Success
            ? ChannelDeliveryResult.Successful(result.Channel, result.DeliveryReference)
            : ChannelDeliveryResult.Failed(
                result.Channel,
                result.Error ?? "Discord delivery failed.",
                result.IsRetryable,
                result.SuggestedRetryDelay);
    }
}
