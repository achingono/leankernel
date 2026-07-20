using LeanKernel.Services.Common.Scheduler;

namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Executes a scheduled job by dispatching to a registered handler.
/// </summary>
public interface IScheduledJobExecutor
{
    /// <summary>
    /// Executes one scheduled job instance.
    /// </summary>
    Task ExecuteAsync(ScheduledJobDefinition job, CancellationToken cancellationToken = default);
}
