using LeanKernel.Entities;

namespace LeanKernel.Logic.Tools.DocumentIngestion;

/// <summary>
/// Durable queue abstraction for document ingestion jobs backed by DB.
/// </summary>
public interface IDocumentIngestionQueue
{
    /// <summary>
    /// Enqueues a new document ingestion job.
    /// </summary>
    /// <param name="job">The job to enqueue.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(DocumentIngestionJob job, CancellationToken ct = default);

    /// <summary>
    /// Tries to claim the next pending job for processing.
    /// </summary>
    /// <param name="workerId">The worker identifier.</param>
    /// <param name="leaseDuration">The lease duration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The claimed job entity, or <c>null</c> if none available.</returns>
    Task<DocumentIngestionJobEntity?> TryClaimNextAsync(string workerId, TimeSpan leaseDuration, CancellationToken ct = default);

    /// <summary>
    /// Marks a job as completed with the given ingestion result.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="result">The ingestion result.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CompleteAsync(Guid jobId, IngestionResult result, CancellationToken ct = default);

    /// <summary>
    /// Marks a job as failed with an error message, optionally scheduling a retry.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="error">The error message.</param>
    /// <param name="retryAt">Optional retry time; <c>null</c> to poison the job.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task FailAsync(Guid jobId, string error, DateTime? retryAt = null, CancellationToken ct = default);

    /// <summary>
    /// Recovers stale jobs with expired leases by resetting them to <c>Pending</c>.
    /// Called on service startup to reclaim jobs left in <c>Processing</c> state after a crash.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of recovered jobs.</returns>
    Task<int> RecoverStaleLeasesAsync(CancellationToken ct = default);
}
