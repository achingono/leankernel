namespace LeanKernel.Gateway.Channels;

/// <summary>
/// Configuration for the channel transport layer.
/// </summary>
public sealed class ChannelsSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the channel hosted service is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the tenant host name used for channel-originated traffic.
    /// </summary>
    public string TenantHostName { get; set; } = "localhost";

    /// <summary>
    /// Gets or sets the Signal adapter configuration.
    /// </summary>
    public SignalSettings Signal { get; set; } = new();

    /// <summary>
    /// Gets or sets the per-channel authentication configurations.
    /// </summary>
    public List<ChannelAuthSettings> ChannelAuth { get; set; } = [];

    /// <summary>
    /// Gets or sets the typing indicator keep-alive configuration.
    /// </summary>
    public TypingSettings Typing { get; set; } = new();
}

/// <summary>
/// Configuration for the Signal channel adapter.
/// </summary>
public sealed class SignalSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the Signal channel is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the Signal daemon HTTP base URL.
    /// </summary>
    public string DaemonUrl { get; set; } = "http://localhost:8080";

    /// <summary>
    /// Gets or sets the comma-separated list of Signal phone numbers to listen on.
    /// </summary>
    public string PhoneNumbers { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum number of reconnect attempts before giving up. 0 = unlimited.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 10;

    /// <summary>
    /// Gets or sets the base reconnect delay in seconds (doubles on each attempt, capped at 300s).
    /// </summary>
    public double ReconnectDelaySeconds { get; set; } = 2;

    /// <summary>
    /// Parses the configured phone numbers into a list.
    /// </summary>
    public IReadOnlyList<string> GetPhoneNumbers()
    {
        if (string.IsNullOrWhiteSpace(PhoneNumbers))
        {
            return [];
        }

        return PhoneNumbers
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }
}

/// <summary>
/// Per-channel authentication settings.
/// </summary>
public sealed class ChannelAuthSettings
{
    /// <summary>
    /// Gets or sets the channel identifier this auth config applies to.
    /// </summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether sender authentication is required.
    /// When false, all messages are accepted without sender verification.
    /// </summary>
    public bool RequireAuth { get; set; } = true;

    /// <summary>
    /// Gets or sets the list of allowed sender identifiers. Unknown senders are rejected.
    /// </summary>
    public List<string> AllowedSenders { get; set; } = [];
}

/// <summary>
/// Configuration for typing indicator keep-alive.
/// </summary>
public sealed class TypingSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether typing indicators are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the keep-alive interval in seconds.
    /// </summary>
    public double KeepAliveSeconds { get; set; } = 5;

    /// <summary>
    /// Gets or sets the timeout in seconds for the stop-typing signal.
    /// </summary>
    public double StopTimeoutSeconds { get; set; } = 5;
}
