using LeanKernel.Abstractions.Models;

namespace LeanKernel.Tools;

/// <summary>
/// Provides background document ingestion queue operations.
/// </summary>
public interface IDocumentIngestionQueue
{
    /// <summary>
    /// Queues a document for background ingestion and returns the job.
    /// </summary>
    /// <param name="filename">The document filename.</param>
    /// <param name="fileContent">The document content stream.</param>
    /// <param name="title">The optional document title.</param>
    /// <param name="tags">The document tags.</param>
    /// <returns>The queued job with JobId and status.</returns>
    DocumentIngestionJob Queue(
        string filename,
        Stream fileContent,
        string? title,
        List<string> tags);

    /// <summary>
    /// Gets the status and result of a previously queued job.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <returns>The job if found; null otherwise.</returns>
    DocumentIngestionJob? GetJobStatus(string jobId);

    /// <summary>
    /// Gets the count of pending jobs in the queue.
    /// </summary>
    int PendingCount { get; }
}
