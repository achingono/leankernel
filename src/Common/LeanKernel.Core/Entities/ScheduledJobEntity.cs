namespace LeanKernel.Entities;

/// <summary>
/// Represents a scheduled cron job persisted in the database.
/// </summary>
public sealed class ScheduledJobEntity
{
    /// <summary>
    /// Gets or sets the unique job identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the human-readable job name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cron expression defining the schedule.
    /// </summary>
    public string CronExpression { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the job type discriminator (e.g. "DreamCycle").
    /// </summary>
    public string JobType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON configuration for the job handler.
    /// </summary>
    public string ConfigurationJson { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tenant identifier for identity partitioning.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the job is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the last execution timestamp.
    /// </summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>
    /// Gets or sets the next scheduled execution timestamp.
    /// </summary>
    public DateTime NextRunAt { get; set; }

    /// <summary>
    /// Gets or sets the job creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the last update timestamp.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}