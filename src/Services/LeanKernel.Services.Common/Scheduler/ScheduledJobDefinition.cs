namespace LeanKernel.Services.Common.Scheduler;

/// <summary>
/// Runtime representation of a scheduled job definition.
/// </summary>
public sealed class ScheduledJobDefinition
{
    /// <summary>
    /// Gets or sets the unique job identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the optional tenant scope for the job.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the optional channel scope for the job.
    /// </summary>
    public Guid? ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the job.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cron expression evaluated in UTC.
    /// </summary>
    public string Cron { get; set; } = "*/5 * * * *";

    /// <summary>
    /// Gets or sets a value indicating whether the job is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the job type used for handler dispatch.
    /// </summary>
    public string JobType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional JSON payload.
    /// </summary>
    public string? Payload { get; set; }
}
