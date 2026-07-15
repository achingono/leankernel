using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Gateway.Channels;

/// <summary>
/// Fail-closed per-channel sender authentication.
/// Unknown senders are rejected and never processed.
/// </summary>
public sealed class ChannelAuthenticator
{
    private readonly ChannelsSettings _config;
    private readonly ILogger<ChannelAuthenticator> _logger;

    public ChannelAuthenticator(
        IOptions<ChannelsSettings> config,
        ILogger<ChannelAuthenticator> logger)
    {
        _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Authorizes a channel message based on the configured per-channel authentication rules.
    /// </summary>
    /// <param name="message">The message to authorize.</param>
    /// <returns>A result indicating whether the message is authorized and why.</returns>
    public ChannelAuthorizationResult Authorize(ChannelMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var channelConfig = _config.ChannelAuth.FirstOrDefault(candidate =>
            string.Equals(candidate.ChannelId, message.ChannelId, StringComparison.OrdinalIgnoreCase));

        if (channelConfig is null)
        {
            _logger.LogWarning(
                "Rejected channel message for {ChannelId} from {SenderId}: no auth configuration found",
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
            return new ChannelAuthorizationResult(false, "No allowed senders configured for channel.");
        }

        var isAllowed = channelConfig.AllowedSenders.Any(allowedSender =>
            string.Equals(allowedSender, message.SenderId, StringComparison.OrdinalIgnoreCase));

        return isAllowed
            ? new ChannelAuthorizationResult(true, "Sender is authorized for channel.")
            : new ChannelAuthorizationResult(false, "Sender is not authorized for channel.");
    }
}

/// <summary>
/// Represents the result of a channel authorization check.
/// </summary>
/// <param name="IsAuthorized">True if the sender is authorized.</param>
/// <param name="Reason">A description of the authorization result.</param>
public sealed record ChannelAuthorizationResult(bool IsAuthorized, string Reason);
