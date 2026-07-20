using LeanKernel.Data;
using LeanKernel.Services.Common.Scheduler;

using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Loads enabled scheduled jobs from the EF-backed scheduled jobs table.
/// </summary>
/// <param name="dbContextFactory">Factory used to create EF contexts.</param>
public sealed class DbScheduledJobDefinitionProvider(
    IDbContextFactory<EntityContext> dbContextFactory) : IScheduledJobDefinitionProvider
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledJobDefinition>> GetEnabledJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await context.ScheduledJobs
            .AsNoTracking()
            .Where(static job => job.Enabled)
            .Select(static job => new ScheduledJobDefinition
            {
                Id = job.Id,
                TenantId = job.TenantId,
                ChannelId = job.ChannelId,
                Name = job.Name,
                Cron = job.Cron,
                Enabled = job.Enabled,
                JobType = job.JobType,
                Payload = job.Payload
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
