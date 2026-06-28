namespace LeanKernel.Abstractions.Configuration;

public sealed class ChannelsConfig
{
    public bool Enabled { get; set; } = true;
    public SignalChannelConfig Signal { get; set; } = new();
    public List<ChannelAuthConfig> ChannelAuth { get; set; } = [];
}

public sealed class SignalChannelConfig
{
    public bool Enabled { get; set; } = false;
    public string DaemonUrl { get; set; } = "http://signal:8080";
    public string PhoneNumber { get; set; } = string.Empty;
    public List<string> PhoneNumbers { get; set; } = [];
    public int PollIntervalSeconds { get; set; } = 2;
    public int ReconnectDelaySeconds { get; set; } = 5;
    public int MaxReconnectAttempts { get; set; } = 10;

    public List<string> GetPhoneNumbers()
    {
        if (PhoneNumbers.Count > 0) return PhoneNumbers;
        if (!string.IsNullOrWhiteSpace(PhoneNumber)) return [PhoneNumber];
        return [];
    }
}

public sealed class ChannelAuthConfig
{
    public required string ChannelId { get; set; }
    public List<string> AllowedSenders { get; set; } = [];
    public bool RequireAuth { get; set; } = true;
}
