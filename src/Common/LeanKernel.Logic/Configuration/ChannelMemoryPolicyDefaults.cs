namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configures default share/access lists used when a channel has no persisted override.
/// </summary>
public class ChannelMemoryPolicyDefaults
{
    /// <summary>
    /// Gets or sets default Share allow-list values.
    /// </summary>
    public List<string> Share { get; set; } = ["*"];

    /// <summary>
    /// Gets or sets default Access allow-list values.
    /// </summary>
    public List<string> Access { get; set; } = ["*"];
}