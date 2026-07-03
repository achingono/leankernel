namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configuration settings for automatic continuation of long-running tasks.
/// </summary>
public sealed class ContinuationConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether auto-continuation is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of synthetic continuation rounds.
    /// </summary>
    public int MaxAutoContinuations { get; set; } = 3;

    /// <summary>
    /// Gets or sets the maximum elapsed turn duration in seconds.
    /// </summary>
    public int MaxTotalDurationSeconds { get; set; } = 600;

    /// <summary>
    /// Gets or sets a value indicating whether classifier fallback is enabled.
    /// </summary>
    public bool UseClassifier { get; set; }

    /// <summary>
    /// Gets or sets optional continuation phrases appended to the built-in heuristics.
    /// </summary>
    public List<string> ContinuePhrases { get; set; } = [];

    /// <summary>
    /// Gets or sets progress-update configuration.
    /// </summary>
    public ContinuationProgressConfig Progress { get; set; } = new();
}

/// <summary>
/// Configuration settings for in-flight progress updates.
/// </summary>
public sealed class ContinuationProgressConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether progress updates are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the quiet period before sending the first progress update.
    /// </summary>
    public int InitialSilenceSeconds { get; set; } = 20;

    /// <summary>
    /// Gets or sets the minimum interval between progress updates.
    /// </summary>
    public int MinIntervalSeconds { get; set; } = 45;

    /// <summary>
    /// Gets or sets the heartbeat interval when no tool/progress events are received.
    /// </summary>
    public int HeartbeatSeconds { get; set; } = 90;
}
