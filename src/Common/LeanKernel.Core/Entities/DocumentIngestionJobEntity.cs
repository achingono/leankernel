namespace LeanKernel.Entities;

/// <summary>
/// Represents a durable document ingestion job persisted in the database-backed queue.
/// Tracks status, retry count, lease ownership, and scheduling metadata.
/// </summary>
public sealed class DocumentIngestionJobEntity
{
    /// <summary>
    /// Gets or sets the unique job identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the tenant identifier for identity partitioning.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the person identifier for cross-channel identity linking.
    /// </summary>
    public Guid PersonId { get; set; }

    /// <summary>
    /// Gets or sets the channel identifier.
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the availability scope string (Tenant, User, or Channel).
    /// </summary>
    public string AvailabilityScope { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ingestion source discriminator (ChannelAttachment, WatchedFile, Upload).
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the staged file path on disk.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the original file name.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MIME content type.
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SHA-256 content fingerprint.
    /// </summary>
    public string? Fingerprint { get; set; }

    /// <summary>
    /// Gets or sets the job status (Pending, InProgress, Completed, Failed, Poisoned).
    /// </summary>
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// Gets or sets the number of processing attempts.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Gets or sets the last error message, if any.
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
    /// Gets or sets the scheduled time for the next retry attempt.
    /// </summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>
    /// Gets or sets the job creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the last update timestamp.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
