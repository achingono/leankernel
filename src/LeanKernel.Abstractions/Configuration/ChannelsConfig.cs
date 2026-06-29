namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configuration settings for channels.
/// </summary>
public sealed class ChannelsConfig
{
    /// <summary>
    /// Gets or sets whether channels are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the signal channel configuration.
    /// </summary>
    public SignalChannelConfig Signal { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of channel authentication configurations.
    /// </summary>
    public List<ChannelAuthConfig> ChannelAuth { get; set; } = [];
}

/// <summary>
/// Configuration settings for signal channels.
/// </summary>
public sealed class SignalChannelConfig
{
    /// <summary>
    /// Gets or sets whether the signal channel is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the daemon URL.
    /// </summary>
    public string DaemonUrl { get; set; } = "http://signal:8080";

    /// <summary>
    /// Gets or sets the phone number.
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of phone numbers.
    /// </summary>
    public List<string> PhoneNumbers { get; set; } = [];

    /// <summary>
    /// Gets or sets the polling interval in seconds.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// Gets or sets the reconnection delay in seconds.
    /// </summary>
    public int ReconnectDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Gets or sets the maximum number of reconnection attempts.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 10;

    /// <summary>
    /// Returns the list of phone numbers, considering both the PhoneNumber and PhoneNumbers properties.
    /// </summary>
    /// <returns>A list of phone numbers.</returns>
    public List<string> GetPhoneNumbers()
    {
        if (PhoneNumbers.Count > 0) return PhoneNumbers;
        if (!string.IsNullOrWhiteSpace(PhoneNumber)) return [PhoneNumber];
        return [];
    }
}

/// <summary>
/// Configuration settings for channel authentication.
/// </summary>
public sealed class ChannelAuthConfig
{
    /// <summary>
    /// Gets or sets the unique identifier for the channel.
    /// </summary>
    public required string ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the list of allowed senders for the channel.
    /// </summary>
    public List<string> AllowedSenders { get; set; } = [];

    /// <summary>
    /// Gets or sets whether authentication is required for the channel.
    /// </summary>
    public bool RequireAuth { get; set; } = true;
}
