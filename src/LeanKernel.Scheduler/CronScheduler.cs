using LeanKernel.Core.Interfaces;

namespace LeanKernel.Scheduler;

/// <summary>
/// Cron-based scheduler for proactive tasks.
/// Uses NCrontab for expression parsing.
/// </summary>
public sealed class CronScheduler : IScheduler
{
    private readonly Dictionary<string, (string Cron, CancellationTokenSource Cts)> _jobs = [];

    public Task ScheduleAsync(string jobId, string cronExpression, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        if (_jobs.ContainsKey(jobId))
            throw new InvalidOperationException($"Job '{jobId}' is already scheduled");

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _jobs[jobId] = (cronExpression, cts);

        // TODO: Phase 5 — implement NCrontab-based scheduling loop
        return Task.CompletedTask;
    }

    public Task CancelAsync(string jobId, CancellationToken ct)
    {
        if (_jobs.Remove(jobId, out var job))
        {
            job.Cts.Cancel();
            job.Cts.Dispose();
        }
        return Task.CompletedTask;
    }

    public IReadOnlyList<string> ListScheduledJobs() => _jobs.Keys.ToList();
}
