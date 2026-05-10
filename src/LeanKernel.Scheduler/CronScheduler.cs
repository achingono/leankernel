using Microsoft.Extensions.Logging;
using NCrontab;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Scheduler;

/// <summary>
/// Cron-based scheduler for proactive tasks.
/// Uses NCrontab for expression parsing and a lightweight timer loop.
/// </summary>
public sealed class CronScheduler : IScheduler
{
    private readonly Dictionary<string, ScheduledJob> _jobs = [];
    private readonly ILogger<CronScheduler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CronScheduler" /> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public CronScheduler(ILogger<CronScheduler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Executes the schedule async operation.
    /// </summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="cronExpression">The cron expression.</param>
    /// <param name="action">The action.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task ScheduleAsync(string jobId, string cronExpression, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        if (_jobs.ContainsKey(jobId))
            throw new InvalidOperationException($"Job '{jobId}' is already scheduled");

        var schedule = CrontabSchedule.Parse(cronExpression);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var job = new ScheduledJob(jobId, cronExpression, schedule, action, cts);
        _jobs[jobId] = job;

        _ = Task.Run(() => RunJobLoopAsync(job), cts.Token);

        _logger.LogInformation("Scheduled job {JobId} with cron: {Cron}", jobId, cronExpression);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Executes the cancel async operation.
    /// </summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task CancelAsync(string jobId, CancellationToken ct)
    {
        if (_jobs.Remove(jobId, out var job))
        {
            job.Cts.Cancel();
            job.Cts.Dispose();
            _logger.LogInformation("Cancelled job {JobId}", jobId);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Executes the list scheduled jobs operation.
    /// </summary>
    /// <returns>The operation result.</returns>
    public IReadOnlyList<string> ListScheduledJobs() => _jobs.Keys.ToList();

    private async Task RunJobLoopAsync(ScheduledJob job)
    {
        var ct = job.Cts.Token;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                var next = job.Schedule.GetNextOccurrence(now);
                var delay = next - now;

                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, ct);

                if (ct.IsCancellationRequested) break;

                try
                {
                    _logger.LogDebug("Executing job {JobId}", job.Id);
                    await job.Action(ct);
                    _logger.LogDebug("Job {JobId} completed", job.Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Job {JobId} failed", job.Id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Scheduled job {JobId} cancelled", job.Id);
        }
    }

    private sealed record ScheduledJob(
        string Id,
        string CronExpression,
        CrontabSchedule Schedule,
        Func<CancellationToken, Task> Action,
        CancellationTokenSource Cts);
}
