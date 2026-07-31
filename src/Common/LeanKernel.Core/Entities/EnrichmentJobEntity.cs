namespace LeanKernel.Entities;

/// <summary>
/// Represents a durable enrichment job persisted in the database-backed queue.
/// </summary>
public sealed class EnrichmentJobEntity
{
    /// <summary>
    /// Gets or sets the unique job identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the originating ingestion job identifier.
    /// </summary>
    public Guid IngestionJobId { get; set; }

    /// <summary>
    /// Gets or sets the tenant identifier for identity partitioning.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the person identifier.
    /// </summary>
    public Guid PersonId { get; set; }

    /// <summary>
    /// Gets or sets the channel identifier.
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the availability scope string.
    /// </summary>
    public string AvailabilityScope { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the staged file path.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the original file name.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content fingerprint.
    /// </summary>
    public string? Fingerprint { get; set; }

    /// <summary>
    /// Gets or sets the job status (Pending, Processing, Completed, Failed, Poisoned).
    /// </summary>
    public string Status { get; set; } = Constants.JobStatus.Pending;

    /// <summary>
    /// Gets or sets the number of processing attempts.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Gets or sets the last error message.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets the worker identifier holding the current lease.
    /// </summary>
    public string? LeaseOwner { get; set; }

    /// <summary>
    /// Gets or sets the lease expiry timestamp.
    /// </summary>
    public DateTime? LeaseExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the scheduled retry timestamp.
    /// </summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>
    /// Gets or sets the linked Dream run identifier.
    /// </summary>
    public Guid? DreamRunId { get; set; }

    /// <summary>
    /// Gets or sets the job creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the last update timestamp.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}