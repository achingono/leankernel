using LeanKernel.Abstractions.Models;

namespace LeanKernel.Tools;

/// <summary>
/// Provides background document ingestion queue operations.
/// </summary>
public interface IDocumentIngestionQueue
{
    /// <summary>
    /// Queues a document for background ingestion.
    /// Throws <see cref="InvalidOperationException"/> if the queue is full.
    /// </summary>
    DocumentIngestionJob Queue(
        string filename,
        Stream fileContent,
        string? title,
        List<string> tags);

    /// <summary>
    /// Queues an existing file path for background ingestion.
    /// Throws <see cref="InvalidOperationException"/> if the queue is full.
    /// </summary>
    PathDocumentIngestionJob QueuePath(
        string sourcePath,
        string? title,
        List<string> tags);

    /// <summary>
    /// Asynchronously queues a document with backpressure and timeout.
    /// </summary>
    Task<EnqueueResult> QueueAsync(
        string filename,
        Stream fileContent,
        string? title,
        List<string> tags,
        CancellationToken ct = default);

    /// <summary>
    /// Asynchronously queues an existing file path with backpressure and timeout.
    /// </summary>
    Task<EnqueueResult> QueuePathAsync(
        string sourcePath,
        string? title,
        List<string> tags,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the status and result of a previously queued job.
    /// </summary>
    DocumentIngestionJob? GetJobStatus(string jobId);

    /// <summary>
    /// Gets the count of pending jobs in the queue.
    /// </summary>
    int PendingCount { get; }
}
