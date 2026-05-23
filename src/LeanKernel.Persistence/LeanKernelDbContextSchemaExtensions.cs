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
            CREATE TABLE IF NOT EXISTS \"ScheduledJobExecutions\" (
                \"Id\" uuid NOT NULL,
                \"JobName\" text NOT NULL,
                \"ScheduledAt\" timestamp with time zone NOT NULL,
                \"StartedAt\" timestamp with time zone NOT NULL,
                \"CompletedAt\" timestamp with time zone NULL,
                \"Success\" boolean NOT NULL,
                \"Result\" text NULL,
                \"Error\" text NULL,
                CONSTRAINT \"PK_ScheduledJobExecutions\" PRIMARY KEY (\"Id\")
            )
            """,
            ct).ConfigureAwait(false);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_ScheduledJobExecutions_JobName\" ON \"ScheduledJobExecutions\" (\"JobName\")",
            ct).ConfigureAwait(false);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_ScheduledJobExecutions_JobName_ScheduledAt\" ON \"ScheduledJobExecutions\" (\"JobName\", \"ScheduledAt\")",
            ct).ConfigureAwait(false);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_ScheduledJobExecutions_StartedAt\" ON \"ScheduledJobExecutions\" (\"StartedAt\")",
            ct).ConfigureAwait(false);
    }
}
