using LeanKernel.Data;
using LeanKernel.Entities;

using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Logic.Tools.DocumentIngestion;

/// <summary>
/// Queue for enqueuing, claiming, completing, and failing document ingestion jobs.
/// Backed by EF Core and the <c>DocumentIngestionJobs</c> table.
/// </summary>
public sealed class DocumentIngestionQueue : IDocumentIngestionQueue
{
    private readonly IDbContextFactory<EntityContext> _contextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentIngestionQueue"/> class.
    /// </summary>
    /// <param name="contextFactory">The EF Core context factory.</param>
    public DocumentIngestionQueue(IDbContextFactory<EntityContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(DocumentIngestionJob job, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var entity = new DocumentIngestionJobEntity
        {
            Id = Guid.NewGuid(),
            FilePath = job.FilePath,
            FileName = job.FileName,
            ContentType = job.ContentType,
            TenantId = job.TenantId,
            UserId = job.UserId,
            PersonId = job.PersonId,
            ChannelId = job.ChannelId,
            AvailabilityScope = job.AvailabilityScope.ToString(),
            Source = job.Source.ToString(),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            AttemptCount = 0,
        };

        context.DocumentIngestionJobs.Add(entity);
        await context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<DocumentIngestionJobEntity?> TryClaimNextAsync(string workerId, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        var leaseExpiry = now + leaseDuration;

        var rawId = await context.DocumentIngestionJobs
            .Where(j => j.Status == "Pending"
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
            UPDATE "DocumentIngestionJobs"
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

        var claimed = await context.DocumentIngestionJobs.FindAsync(new object[] { rawId }, ct);
        return claimed;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(Guid jobId, IngestionResult result, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var entity = await context.DocumentIngestionJobs.FindAsync(new object[] { jobId }, ct);
        if (entity == null)
        {
            return;
        }

        entity.Status = result.Success ? "Completed" : "Failed";
        entity.UpdatedAt = DateTime.UtcNow;
        entity.LeaseExpiresAt = null;
        entity.LastError = result.IsDuplicate ? "Duplicate" : null;

        await context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task FailAsync(Guid jobId, string error, DateTime? retryAt = null, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var entity = await context.DocumentIngestionJobs.FindAsync(new object[] { jobId }, ct);
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
            entity.Status = "Pending";
            entity.NextAttemptAt = retryAt.Value;
        }
        else
        {
            entity.Status = "Poisoned";
        }

        await context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<int> RecoverStaleLeasesAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        var stale = await context.DocumentIngestionJobs
            .Where(j => j.Status == "Processing" && j.LeaseExpiresAt <= now)
            .ToListAsync(ct);

        foreach (var job in stale)
        {
            job.Status = "Pending";
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
