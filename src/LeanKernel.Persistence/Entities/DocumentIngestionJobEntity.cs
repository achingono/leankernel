namespace LeanKernel.Persistence.Entities;

/// <summary>
/// Represents a persisted document ingestion job.
/// </summary>
public sealed class DocumentIngestionJobEntity
{
    /// <summary>
    /// Gets or sets the unique job identifier.
    /// </summary>
    public required string JobId { get; set; }

    /// <summary>
    /// Gets or sets the document filename.
    /// </summary>
    public required string Filename { get; set; }

    /// <summary>
    /// Gets or sets the optional document title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the document tags as comma-separated values.
    /// </summary>
    public string Tags { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current job status (Queued, Processing, Completed, Failed, Cancelled).
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Gets or sets the ingestion result as JSON when completed.
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Gets or sets the error message if the job failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets when the job was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets when processing started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// Gets or sets when processing completed or failed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the number of retry attempts.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Gets or sets the source file path for path-based ingestion jobs.
    /// Null for stream-based jobs.
    /// </summary>
    public string? SourcePath { get; set; }
}
