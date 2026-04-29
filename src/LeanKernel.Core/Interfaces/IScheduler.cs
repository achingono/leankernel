namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Cron-based scheduler for proactive tasks.
/// </summary>
public interface IScheduler
{
    Task ScheduleAsync(string jobId, string cronExpression, Func<CancellationToken, Task> action, CancellationToken ct);
    Task CancelAsync(string jobId, CancellationToken ct);
    IReadOnlyList<string> ListScheduledJobs();
}
