using System.Text.Json.Serialization;

namespace LeanKernel.Abstractions.Models;

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

    /// <summary>
    /// Gets or sets the number of retry attempts.
    /// </summary>
    public int RetryCount { get; set; }
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

/// <summary>
/// Represents the result of a successful document ingestion.
/// </summary>
public sealed class DocumentIngestionResult
{
    /// <summary>
    /// Gets the compiled page slug inside the wiki knowledge base.
    /// </summary>
    public required string PageSlug { get; init; }

    /// <summary>
    /// Gets the human-readable document title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the length of the extracted text.
    /// </summary>
    public int ExtractedLength { get; init; }

    /// <summary>
    /// Gets the file path relative to the AllowedRoot.
    /// </summary>
    public required string RelativeFilePath { get; init; }

    /// <summary>
    /// Gets the internal storage path inside GBrain storage.
    /// </summary>
    public required string FileStoragePath { get; init; }
}
