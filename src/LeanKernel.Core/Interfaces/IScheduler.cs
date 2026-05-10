namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Cron-based scheduler for proactive tasks.
/// </summary>
public interface IScheduler
{
    /// <summary>
    /// Schedules an asynchronous job using the provided cron expression.
    /// </summary>
    Task ScheduleAsync(string jobId, string cronExpression, Func<CancellationToken, Task> action, CancellationToken ct);
    /// <summary>
    /// Cancels a scheduled job by identifier.
    /// </summary>
    Task CancelAsync(string jobId, CancellationToken ct);
    /// <summary>
    /// Lists identifiers for currently scheduled jobs.
    /// </summary>
    IReadOnlyList<string> ListScheduledJobs();
}
