using LeanKernel.Entities;

namespace LeanKernel.Logic.Tools.DocumentIngestion;

/// <summary>
/// Durable queue abstraction for enrichment jobs backed by DB.
/// </summary>
public interface IEnrichmentQueue
{
    /// <summary>
    /// Enqueues a new enrichment job.
    /// </summary>
    /// <param name="job">The job to enqueue.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(EnrichmentJob job, CancellationToken ct = default);

    /// <summary>
    /// Tries to claim the next pending enrichment job for processing.
    /// </summary>
    /// <param name="workerId">The worker identifier.</param>
    /// <param name="leaseDuration">The lease duration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The claimed job entity, or <c>null</c> if none available.</returns>
    Task<EnrichmentJobEntity?> TryClaimNextAsync(string workerId, TimeSpan leaseDuration, CancellationToken ct = default);

    /// <summary>
    /// Marks an enrichment job as completed with the given result.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="result">The enrichment result.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CompleteAsync(Guid jobId, EnrichmentResult result, CancellationToken ct = default);

    /// <summary>
    /// Marks an enrichment job as failed with an error message, optionally scheduling a retry.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="error">The error message.</param>
    /// <param name="retryAt">Optional retry time; <c>null</c> to poison the job.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task FailAsync(Guid jobId, string error, DateTime? retryAt = null, CancellationToken ct = default);

    /// <summary>
    /// Recovers stale enrichment jobs with expired leases by resetting them to <c>Pending</c>.
    /// Called on service startup to reclaim jobs left in <c>Processing</c> state after a crash.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of recovered jobs.</returns>
    Task<int> RecoverStaleLeasesAsync(CancellationToken ct = default);
}