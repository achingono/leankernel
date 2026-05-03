using Microsoft.Extensions.Logging;

namespace LeanKernel.Host.Services.Channels;

/// <summary>
/// Registry and factory for message channel adapters.
/// Resolves channels by name and manages lifecycle.
/// </summary>
public sealed class ChannelRegistry
{
    private readonly Dictionary<string, IMessageChannel> _channels;
    private readonly ILogger<ChannelRegistry> _logger;

    public ChannelRegistry(ILogger<ChannelRegistry> logger)
    {
        _logger = logger;
        _channels = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Register a channel adapter.
    /// </summary>
    public void RegisterChannel(IMessageChannel channel)
    {
        if (_channels.ContainsKey(channel.Name))
        {
            _logger.LogWarning("Channel '{ChannelName}' already registered, overwriting", channel.Name);
        }

        _channels[channel.Name] = channel;
        _logger.LogInformation("Registered message channel: {ChannelName} (configured: {IsConfigured})", 
            channel.Name, channel.IsConfigured);
    }

    /// <summary>
    /// Get a channel adapter by name.
    /// Returns null if channel not registered.
    /// </summary>
    public IMessageChannel? GetChannel(string channelName)
    {
        if (string.IsNullOrWhiteSpace(channelName))
        {
            _logger.LogWarning("Attempted to get channel with null or empty name");
            return null;
        }

        if (!_channels.TryGetValue(channelName, out var channel))
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
    /// Get all registered channels.
    /// </summary>
    public IReadOnlyDictionary<string, IMessageChannel> GetAllChannels() =>
        _channels.AsReadOnly();

    /// <summary>
    /// Get count of registered channels.
    /// </summary>
    public int RegisteredChannelCount => _channels.Count;

    /// <summary>
    /// Check if a channel is registered and configured.
    /// </summary>
    public bool IsChannelAvailable(string channelName) =>
        GetChannel(channelName) != null;
}
