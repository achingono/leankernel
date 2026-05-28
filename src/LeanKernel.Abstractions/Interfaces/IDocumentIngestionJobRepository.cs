using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Provides database access for document ingestion jobs.
/// </summary>
public interface IDocumentIngestionJobRepository
{
    /// <summary>
    /// Creates or updates a job in the database.
    /// </summary>
    Task SaveJobAsync(DocumentIngestionJob job, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a job by ID.
    /// </summary>
    Task<DocumentIngestionJob?> GetJobAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all pending jobs (status = Queued or Processing).
    /// </summary>
    Task<IReadOnlyList<DocumentIngestionJob>> GetPendingJobsAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves jobs that failed and can be retried.
    /// </summary>
    Task<IReadOnlyList<DocumentIngestionJob>> GetFailedJobsForRetryAsync(int maxRetries = 3, CancellationToken ct = default);

    /// <summary>
    /// Updates job status to Processing.
    /// </summary>
    Task UpdateJobStatusToProcessingAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// Updates job with completion result.
    /// </summary>
    Task UpdateJobCompletedAsync(string jobId, DocumentIngestionResult result, CancellationToken ct = default);

    /// <summary>
    /// Updates job with failure information.
    /// </summary>
    Task UpdateJobFailedAsync(string jobId, string errorMessage, CancellationToken ct = default);

    /// <summary>
    /// Increments retry count for a job.
    /// </summary>
    Task IncrementRetryCountAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// Deletes a completed or failed job after archival period.
    /// </summary>
    Task DeleteJobAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// Deletes jobs older than the specified retention period.
    /// </summary>
    Task DeleteOldJobsAsync(TimeSpan retentionPeriod, CancellationToken ct = default);
}
