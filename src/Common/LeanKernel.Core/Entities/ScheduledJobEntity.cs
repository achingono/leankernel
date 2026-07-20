namespace LeanKernel.Entities;

/// <summary>
/// Represents a user-managed scheduled learning job scoped to a tenant and/or channel.
/// </summary>
public class ScheduledJobEntity : IEntity
{
    /// <summary>
    /// Gets or sets the unique scheduled job identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the optional tenant scope for this job.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the optional channel scope for this job.
    /// </summary>
    public Guid? ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the job within its scope.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cron expression evaluated in UTC.
    /// </summary>
    public string Cron { get; set; } = "*/5 * * * *";

    /// <summary>
    /// Gets or sets a value indicating whether this job is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the scheduled job handler type.
    /// </summary>
    public string JobType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional JSON payload for the job.
    /// </summary>
    public string? Payload { get; set; }

    /// <summary>
    /// Gets or sets when this job was created.
    /// </summary>
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when this job was last updated.
    /// </summary>
    public DateTime? UpdatedOn { get; set; }

    /// <summary>
    /// Gets or sets the optional tenant navigation.
    /// </summary>
    public virtual TenantEntity? Tenant { get; set; }

    /// <summary>
    /// Gets or sets the optional channel navigation.
    /// </summary>
    public virtual ChannelEntity? Channel { get; set; }
}
