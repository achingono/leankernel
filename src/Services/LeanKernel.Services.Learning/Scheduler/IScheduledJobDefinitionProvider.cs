using LeanKernel.Services.Common.Scheduler;

namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Provides enabled scheduled job definitions from persistence.
/// </summary>
public interface IScheduledJobDefinitionProvider
{
    /// <summary>
    /// Retrieves all enabled scheduled jobs.
    /// </summary>
    Task<IReadOnlyList<ScheduledJobDefinition>> GetEnabledJobsAsync(CancellationToken cancellationToken = default);
}
