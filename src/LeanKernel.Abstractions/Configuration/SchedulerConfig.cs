namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configures proactive scheduled job execution.
/// </summary>
public sealed class SchedulerConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether the scheduler is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the interval, in seconds, between scheduler ticks.
    /// </summary>
    public int TickIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the maximum number of scheduled jobs that may run concurrently.
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 2;

    /// <summary>
    /// Gets or sets the default timezone identifier used for cron evaluation.
    /// </summary>
    public string DefaultTimezone { get; set; } = "UTC";

    /// <summary>
    /// Gets or sets the configured scheduled jobs.
    /// </summary>
    public List<ScheduledJobDefinition> Jobs { get; set; } = [];
}

/// <summary>
/// Describes a configured scheduled job.
/// </summary>
public sealed class ScheduledJobDefinition
{
    /// <summary>
    /// Gets or sets the unique job name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the cron expression that determines when the job is due.
    /// </summary>
    public required string CronExpression { get; set; }

    /// <summary>
    /// Gets or sets the job type.
    /// </summary>
    public required string JobType { get; set; }

    /// <summary>
    /// Gets or sets the optional prompt used by prompt-driven job types.
    /// </summary>
    public string? Prompt { get; set; }

    /// <summary>
    /// Gets or sets the optional channel identifier used for proactive agent prompts.
    /// </summary>
    public string? ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the optional sender or user identifier used for proactive agent prompts.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the job is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets additional job parameters.
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = [];
}
