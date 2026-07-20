namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configures channel terminals, sender binding behavior, and memory policy defaults.
/// </summary>
public class ChannelSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether channel claim processing is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets trusted channel issuers for JWT validation.
    /// </summary>
    public List<string> TrustedIssuers { get; set; } = [];

    /// <summary>
    /// Gets or sets memory sharing policy defaults.
    /// </summary>
    public ChannelMemoryPolicyDefaults MemoryPolicyDefaults { get; set; } = new ChannelMemoryPolicyDefaults();
}
