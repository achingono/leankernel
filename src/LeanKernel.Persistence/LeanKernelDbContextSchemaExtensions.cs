using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Persistence;

/// <summary>
/// Provides targeted schema initialization helpers for additive persistence features.
/// </summary>
public static class LeanKernelDbContextSchemaExtensions
{
    /// <summary>
    /// Ensures the scheduler execution table and indexes exist for deployments created before the scheduler was introduced.
    /// </summary>
    /// <param name="dbContext">The database context to initialize.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the scheduler schema is present.</returns>
    public static async Task EnsureSchedulerSchemaAsync(this LeanKernelDbContext dbContext, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE SCHEMA IF NOT EXISTS engine
            """,
            ct).ConfigureAwait(false);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS engine."ScheduledJobExecutions" (
                "Id" uuid NOT NULL,
                "JobName" text NOT NULL,
                "ScheduledAt" timestamp with time zone NOT NULL,
                "StartedAt" timestamp with time zone NOT NULL,
                "CompletedAt" timestamp with time zone NULL,
                "Success" boolean NOT NULL,
                "Result" text NULL,
                "Error" text NULL,
                CONSTRAINT "PK_ScheduledJobExecutions" PRIMARY KEY ("Id")
            )
            """,
            ct).ConfigureAwait(false);
        await dbContext.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_ScheduledJobExecutions_JobName" ON engine."ScheduledJobExecutions" ("JobName")""",
            ct).ConfigureAwait(false);
        await dbContext.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_ScheduledJobExecutions_JobName_ScheduledAt" ON engine."ScheduledJobExecutions" ("JobName", "ScheduledAt")""",
            ct).ConfigureAwait(false);
        await dbContext.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_ScheduledJobExecutions_StartedAt" ON engine."ScheduledJobExecutions" ("StartedAt")""",
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures the DocumentIngestionJobs table has the SourcePath column for path-based ingestion retries.
    /// </summary>
    /// <param name="dbContext">The database context to initialize.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the column is present.</returns>
    public static async Task EnsureDocumentIngestionJobsSourcePathColumnAsync(this LeanKernelDbContext dbContext, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE engine."DocumentIngestionJobs"
            ADD COLUMN IF NOT EXISTS "SourcePath" text NULL
            """,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures the document fingerprints table exists for deduplication.
    /// </summary>
    /// <param name="dbContext">The database context to initialize.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the fingerprint schema is present.</returns>
    public static async Task EnsureFingerprintSchemaAsync(this LeanKernelDbContext dbContext, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE SCHEMA IF NOT EXISTS engine
            """,
            ct).ConfigureAwait(false);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS engine."DocumentFingerprints" (
                "Fingerprint" text NOT NULL,
                "FilePath" text NOT NULL,
                "FileSize" bigint NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_DocumentFingerprints" PRIMARY KEY ("Fingerprint")
            )
            """,
            ct).ConfigureAwait(false);
        await dbContext.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_DocumentFingerprints_FilePath" ON engine."DocumentFingerprints" ("FilePath")""",
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures an index on <c>SessionEntity.UserId</c> for user-scoped session queries and migration lookups.
    /// </summary>
    /// <param name="dbContext">The database context to initialize.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the index is present.</returns>
    public static async Task EnsureUserIdIndexAsync(this LeanKernelDbContext dbContext, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        await dbContext.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_Sessions_UserId" ON engine."Sessions" ("UserId")""",
            ct).ConfigureAwait(false);
    }
}
