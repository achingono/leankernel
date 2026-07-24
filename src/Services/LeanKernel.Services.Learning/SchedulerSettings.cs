namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Configuration options for the scheduler service.
/// </summary>
public sealed class SchedulerSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the scheduler is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the polling interval in seconds for checking scheduled jobs.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the lock timeout in seconds for dream cycle jobs.
    /// </summary>
    public int DreamLockTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets the default dream cycle mode.
    /// </summary>
    public string DefaultDreamMode { get; set; } = "full";
}