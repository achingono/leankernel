namespace LeanKernel.Services.Gateway.Configuration;

/// <summary>
/// Configuration settings for event spine retention and cleanup.
/// </summary>
public sealed class EventRetentionSettings
{
    /// <summary>
    /// Gets or sets the number of days to retain event records before they are eligible for cleanup.
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets the interval in minutes between cleanup cycles.
    /// </summary>
    public int CleanupIntervalMinutes { get; set; } = 60;
}
