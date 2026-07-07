using System.Text.Json;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Persistence;

/// <summary>
/// Provides database-backed persistence for document ingestion jobs.
/// </summary>
public sealed class DocumentIngestionJobRepository(
    LeanKernelDbContext dbContext,
    ILogger<DocumentIngestionJobRepository> logger) : IDocumentIngestionJobRepository
{
    private readonly LeanKernelDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly ILogger<DocumentIngestionJobRepository> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task SaveJobAsync(DocumentIngestionJob job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var entity = await _dbContext.DocumentIngestionJobs.FirstOrDefaultAsync(
            x => x.JobId == job.JobId,
            cancellationToken: ct
        );

        if (entity is null)
        {
            entity = new DocumentIngestionJobEntity
            {
                JobId = job.JobId,
                Filename = job.Filename,
                Title = job.Title,
                Tags = JsonSerializer.Serialize(job.Tags),
                Status = job.Status.ToString(),
                CreatedAt = job.CreatedAt,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                RetryCount = job.RetryCount,
                SourcePath = job is PathDocumentIngestionJob pathJob ? pathJob.SourcePath : null
            };
            if (job.Result is not null)
            {
                entity.Result = JsonSerializer.Serialize(job.Result);
            }
            if (job.ErrorMessage is not null)
            {
                entity.ErrorMessage = job.ErrorMessage;
            }
            _dbContext.DocumentIngestionJobs.Add(entity);
        }
        else
        {
            entity.Status = job.Status.ToString();
            entity.StartedAt = job.StartedAt;
            entity.CompletedAt = job.CompletedAt;
            entity.RetryCount = job.RetryCount;
            if (job is PathDocumentIngestionJob pathJob)
            {
                entity.SourcePath = pathJob.SourcePath;
            }
            if (job.Result is not null)
            {
                entity.Result = JsonSerializer.Serialize(job.Result);
            }
            if (job.ErrorMessage is not null)
            {
                entity.ErrorMessage = job.ErrorMessage;
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogDebug("Saved job {JobId} with status {Status}", job.JobId, job.Status);
    }

    /// <inheritdoc />
    public async Task<DocumentIngestionJob?> GetJobAsync(string jobId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        var entity = await _dbContext.DocumentIngestionJobs.FirstOrDefaultAsync(
            x => x.JobId == jobId,
            cancellationToken: ct
        );

        if (entity is null)
        {
            return null;
        }

        return MapToJob(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentIngestionJob>> GetPendingJobsAsync(CancellationToken ct = default)
    {
        var entities = await _dbContext.DocumentIngestionJobs
            .Where(x => x.Status == DocumentIngestionStatus.Queued.ToString() ||
                        x.Status == DocumentIngestionStatus.Processing.ToString())
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken: ct);

        _logger.LogDebug("Retrieved {Count} pending jobs", entities.Count);
        return entities.Select(MapToJob).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentIngestionJob>> GetFailedJobsForRetryAsync(int maxRetries = 3, CancellationToken ct = default)
    {
        var entities = await _dbContext.DocumentIngestionJobs
            .Where(x => x.Status == DocumentIngestionStatus.Failed.ToString() &&
                        x.RetryCount < maxRetries)
            .OrderBy(x => x.CompletedAt)
            .ToListAsync(cancellationToken: ct);

        _logger.LogDebug("Retrieved {Count} failed jobs for retry", entities.Count);
        return entities.Select(MapToJob).ToList();
    }

    /// <inheritdoc />
    public async Task UpdateJobStatusToProcessingAsync(string jobId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        var entity = await _dbContext.DocumentIngestionJobs.FirstOrDefaultAsync(
            x => x.JobId == jobId,
            cancellationToken: ct
        );

        if (entity is null)
        {
            _logger.LogWarning("Cannot update job {JobId} to Processing: job not found", jobId);
            return;
        }

        entity.Status = DocumentIngestionStatus.Processing.ToString();
        entity.StartedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task UpdateJobCompletedAsync(string jobId, DocumentIngestionResult result, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentNullException.ThrowIfNull(result);

        var entity = await _dbContext.DocumentIngestionJobs.FirstOrDefaultAsync(
            x => x.JobId == jobId,
            cancellationToken: ct
        );

        if (entity is null)
        {
            _logger.LogWarning("Cannot mark job {JobId} as completed: job not found", jobId);
            return;
        }

        var resultJson = JsonSerializer.Serialize(result);
        entity.Status = DocumentIngestionStatus.Completed.ToString();
        entity.CompletedAt = DateTimeOffset.UtcNow;
        entity.Result = resultJson;
        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("Job {JobId} completed successfully", jobId);
    }

    /// <inheritdoc />
    public async Task UpdateJobFailedAsync(string jobId, string errorMessage, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        var entity = await _dbContext.DocumentIngestionJobs.FirstOrDefaultAsync(
            x => x.JobId == jobId,
            cancellationToken: ct
        );

        if (entity is null)
        {
            _logger.LogWarning("Cannot mark job {JobId} as failed: job not found", jobId);
            return;
        }

        entity.Status = DocumentIngestionStatus.Failed.ToString();
        entity.CompletedAt = DateTimeOffset.UtcNow;
        entity.ErrorMessage = errorMessage;
        await _dbContext.SaveChangesAsync(ct);
        _logger.LogWarning("Job {JobId} failed: {Error}", jobId, errorMessage);
    }

    /// <inheritdoc />
    public async Task IncrementRetryCountAsync(string jobId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        var entity = await _dbContext.DocumentIngestionJobs.FirstOrDefaultAsync(
            x => x.JobId == jobId,
            cancellationToken: ct
        );

        if (entity is null)
        {
            _logger.LogWarning("Cannot increment retry count for job {JobId}: job not found", jobId);
            return;
        }

        entity.RetryCount++;
        entity.Status = DocumentIngestionStatus.Queued.ToString();
        await _dbContext.SaveChangesAsync(ct);
        _logger.LogDebug("Incremented retry count for job {JobId} to {RetryCount}", jobId, entity.RetryCount);
    }

    /// <inheritdoc />
    public async Task DeleteJobAsync(string jobId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        var entity = await _dbContext.DocumentIngestionJobs.FirstOrDefaultAsync(
            x => x.JobId == jobId,
            cancellationToken: ct
        );

        if (entity is null)
        {
            _logger.LogWarning("Cannot delete job {JobId}: job not found", jobId);
            return;
        }

        _dbContext.DocumentIngestionJobs.Remove(entity);
        await _dbContext.SaveChangesAsync(ct);
        _logger.LogDebug("Deleted job {JobId}", jobId);
    }

    /// <inheritdoc />
    public async Task DeleteOldJobsAsync(TimeSpan retentionPeriod, CancellationToken ct = default)
    {
        var cutoffDate = DateTimeOffset.UtcNow.Subtract(retentionPeriod);
        var entitiesToDelete = await _dbContext.DocumentIngestionJobs
            .Where(x => (x.Status == DocumentIngestionStatus.Completed.ToString() ||
                         x.Status == DocumentIngestionStatus.Failed.ToString()) &&
                        x.CompletedAt < cutoffDate)
            .ToListAsync(cancellationToken: ct);

        if (entitiesToDelete.Count > 0)
        {
            _dbContext.DocumentIngestionJobs.RemoveRange(entitiesToDelete);
            await _dbContext.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Deleted {Count} old jobs older than {Cutoff}", entitiesToDelete.Count, cutoffDate);
    }

    private static DocumentIngestionJob MapToJob(DocumentIngestionJobEntity entity)
    {
        var status = Enum.TryParse<DocumentIngestionStatus>(entity.Status, out var parsed)
            ? parsed
            : DocumentIngestionStatus.Queued;

        List<string> tags;
        if (string.IsNullOrEmpty(entity.Tags))
        {
            tags = [];
        }
        else
        {
            try
            {
                tags = JsonSerializer.Deserialize<List<string>>(entity.Tags) ?? [];
            }
            catch (JsonException)
            {
                // Fallback for legacy comma-separated format
                tags = entity.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
        }

        DocumentIngestionResult? result = null;
        if (!string.IsNullOrEmpty(entity.Result))
        {
            try
            {
                result = JsonSerializer.Deserialize<DocumentIngestionResult>(entity.Result);
            }
            catch (JsonException)
            {
                // Result deserialization failed - stays null
            }
        }

        if (!string.IsNullOrEmpty(entity.SourcePath))
        {
            return new PathDocumentIngestionJob
            {
                JobId = entity.JobId,
                Filename = entity.Filename,
                Title = entity.Title,
                Tags = tags,
                Status = status,
                SourcePath = entity.SourcePath,
                CreatedAt = entity.CreatedAt,
                StartedAt = entity.StartedAt,
                CompletedAt = entity.CompletedAt,
                ErrorMessage = entity.ErrorMessage,
                RetryCount = entity.RetryCount,
                Result = result
            };
        }

        return new DocumentIngestionJob
        {
            JobId = entity.JobId,
            Filename = entity.Filename,
            Title = entity.Title,
            Tags = tags,
            Status = status,
            CreatedAt = entity.CreatedAt,
            StartedAt = entity.StartedAt,
            CompletedAt = entity.CompletedAt,
            ErrorMessage = entity.ErrorMessage,
            RetryCount = entity.RetryCount,
            Result = result
        };
    }
}
