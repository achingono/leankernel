using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels;

public sealed class ChannelAuthenticator(
    ILogger<ChannelAuthenticator> logger,
    IOptions<ChannelsConfig> config)
{
    private readonly ILogger<ChannelAuthenticator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ChannelsConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;

    public ChannelAuthorizationResult Authorize(ChannelMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var channelConfig = _config.ChannelAuth.FirstOrDefault(candidate =>
            string.Equals(candidate.ChannelId, message.ChannelId, StringComparison.OrdinalIgnoreCase));

        if (channelConfig is null)
        {
            _logger.LogWarning(
                "Rejected channel message for {ChannelId} from {SenderId}: no auth configuration was found",
                message.ChannelId,
                message.SenderId);

            return new ChannelAuthorizationResult(false, "No auth configuration found for channel.");
        }

        if (!channelConfig.RequireAuth)
        {
            return new ChannelAuthorizationResult(true, "Authentication is disabled for channel.");
        }

        if (string.IsNullOrWhiteSpace(message.SenderId))
        {
            return new ChannelAuthorizationResult(false, "Sender id is required for authenticated channels.");
        }

        if (channelConfig.AllowedSenders.Count == 0)
        {
            return new ChannelAuthorizationResult(false, "No allowed senders are configured for channel.");
        }

        var isAllowed = channelConfig.AllowedSenders.Any(allowedSender =>
            string.Equals(allowedSender, message.SenderId, StringComparison.OrdinalIgnoreCase));

        return isAllowed
            ? new ChannelAuthorizationResult(true, "Sender is authorized for channel.")
            : new ChannelAuthorizationResult(false, "Sender is not authorized for channel.");
    }
}

public sealed record ChannelAuthorizationResult(bool IsAuthorized, string Reason);
