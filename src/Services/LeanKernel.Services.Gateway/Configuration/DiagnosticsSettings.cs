namespace LeanKernel.Services.Gateway.Configuration;

/// <summary>
/// Configuration settings for the diagnostics subsystem, including cleanup and retention.
/// </summary>
public sealed class DiagnosticsSettings
{
    /// <summary>
    /// Gets or sets the number of days to retain diagnostic entries before they are eligible for cleanup.
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets the interval in minutes between cleanup cycles.
    /// </summary>
    public int CleanupIntervalMinutes { get; set; } = 60;
}
