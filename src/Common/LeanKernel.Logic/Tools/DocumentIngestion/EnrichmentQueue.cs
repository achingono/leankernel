using LeanKernel.Data;
using LeanKernel.Entities;

using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Logic.Tools.DocumentIngestion;

/// <summary>
/// Queue for enqueuing, claiming, completing, and failing enrichment jobs.
/// Backed by EF Core and the <c>EnrichmentJobs</c> table.
/// </summary>
public sealed class EnrichmentQueue : IEnrichmentQueue
{
    private readonly IDbContextFactory<EntityContext> _contextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnrichmentQueue"/> class.
    /// </summary>
    /// <param name="contextFactory">The EF Core context factory.</param>
    public EnrichmentQueue(IDbContextFactory<EntityContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(EnrichmentJob job, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var entity = new EnrichmentJobEntity
        {
            Id = Guid.NewGuid(),
            IngestionJobId = job.IngestionJobId,
            FilePath = job.FilePath,
            FileName = job.FileName,
            Fingerprint = job.Fingerprint,
            TenantId = job.TenantId,
            UserId = job.UserId,
            PersonId = job.PersonId,
            ChannelId = job.ChannelId,
            AvailabilityScope = job.AvailabilityScope.ToString(),
            Status = Constants.JobStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            AttemptCount = 0,
        };

        context.Set<EnrichmentJobEntity>().Add(entity);
        await context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<EnrichmentJobEntity?> TryClaimNextAsync(string workerId, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        var leaseExpiry = now + leaseDuration;

        var rawId = await context.Set<EnrichmentJobEntity>()
            .Where(j => j.Status == Constants.JobStatus.Pending
                        && (j.NextAttemptAt == null || j.NextAttemptAt <= now)
                        && (j.LeaseExpiresAt == null || j.LeaseExpiresAt <= now))
            .OrderBy(j => j.CreatedAt)
            .Select(j => j.Id)
            .FirstOrDefaultAsync(ct);

        if (rawId == Guid.Empty)
        {
            return null;
        }

        var rows = await context.Database.ExecuteSqlRawAsync(
            """
            UPDATE "EnrichmentJobs"
            SET "Status" = 'Processing',
                "LeaseOwner" = {0},
                "LeaseExpiresAt" = {1},
                "UpdatedAt" = {2}
            WHERE "Id" = {3}
              AND "Status" = 'Pending'
            """,
            new object[] { workerId, leaseExpiry, now, rawId }, ct);

        if (rows == 0)
        {
            return null;
        }

        var claimed = await context.Set<EnrichmentJobEntity>().FindAsync(new object[] { rawId }, ct);
        return claimed;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(Guid jobId, EnrichmentResult result, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var entity = await context.Set<EnrichmentJobEntity>().FindAsync(new object[] { jobId }, ct);
        if (entity == null)
        {
            return;
        }

        entity.Status = result.Success ? Constants.JobStatus.Completed : Constants.JobStatus.Failed;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.LeaseExpiresAt = null;
        entity.DreamRunId = result.DreamRunId;

        await context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task FailAsync(Guid jobId, string error, DateTime? retryAt = null, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var entity = await context.Set<EnrichmentJobEntity>().FindAsync(new object[] { jobId }, ct);
        if (entity == null)
        {
            return;
        }

        entity.AttemptCount++;
        entity.LastError = error;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.LeaseExpiresAt = null;

        if (retryAt.HasValue && entity.AttemptCount < 5)
        {
            entity.Status = Constants.JobStatus.Pending;
            entity.NextAttemptAt = retryAt.Value;
        }
        else
        {
            entity.Status = Constants.JobStatus.Poisoned;
        }

        await context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<int> RecoverStaleLeasesAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        var stale = await context.Set<EnrichmentJobEntity>()
            .Where(j => j.Status == Constants.JobStatus.Processing && j.LeaseExpiresAt <= now)
            .ToListAsync(ct);

        foreach (var job in stale)
        {
            job.Status = Constants.JobStatus.Pending;
            job.LeaseExpiresAt = null;
            job.LeaseOwner = null;
            job.UpdatedAt = now;
        }

        if (stale.Count > 0)
        {
            await context.SaveChangesAsync(ct);
        }

        return stale.Count;
    }
}