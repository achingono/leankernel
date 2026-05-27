using System.Text.Json.Serialization;

namespace LeanKernel.Tools;

/// <summary>
/// Represents a document queued for background ingestion.
/// </summary>
public sealed class DocumentIngestionJob
{
    /// <summary>
    /// Gets the unique job identifier.
    /// </summary>
    public string JobId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets the document filename.
    /// </summary>
    public string Filename { get; init; } = string.Empty;

    /// <summary>
    /// Gets the document content stream (temporary, processed during ingestion).
    /// </summary>
    [JsonIgnore]
    public Stream FileContent { get; init; } = null!;

    /// <summary>
    /// Gets the optional document title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the document tags.
    /// </summary>
    public List<string> Tags { get; init; } = [];

    /// <summary>
    /// Gets the current job status.
    /// </summary>
    public DocumentIngestionStatus Status { get; set; } = DocumentIngestionStatus.Queued;

    /// <summary>
    /// Gets the ingestion result when completed.
    /// </summary>
    public DocumentIngestionResult? Result { get; set; }

    /// <summary>
    /// Gets the error message if the job failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets when the job was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets when processing started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// Gets when processing completed or failed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>
/// Represents the status of a document ingestion job.
/// </summary>
public enum DocumentIngestionStatus
{
    /// <summary>Queued and waiting for processing.</summary>
    Queued,

    /// <summary>Currently being processed.</summary>
    Processing,

    /// <summary>Successfully completed.</summary>
    Completed,

    /// <summary>Failed during processing.</summary>
    Failed,

    /// <summary>Job was cancelled.</summary>
    Cancelled
}
