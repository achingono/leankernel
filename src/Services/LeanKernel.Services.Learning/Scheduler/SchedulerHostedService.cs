using LeanKernel.Services.Common.Scheduler;
using LeanKernel.Services.Learning.Configuration;

            foreach (var job in enabledJobs)
using Microsoft.Extensions.Options;

namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Background scheduler that loads enabled jobs, evaluates cron schedules, and executes due jobs.
/// </summary>
/// <param name="serviceScopeFactory">Creates scoped services for loading and executing jobs.</param>
/// <param name="timeProvider">Provides UTC time for schedule evaluation.</param>
/// <param name="options">Runtime scheduler options.</param>
/// <param name="logger">Logger instance.</param>
public sealed class SchedulerHostedService(
    IServiceScopeFactory serviceScopeFactory,
    TimeProvider timeProvider,
    IOptions<SchedulerRuntimeOptions> options,
    ILogger<SchedulerHostedService> logger) : BackgroundService
{
    private readonly SchedulerRuntimeOptions _options = options.Value;
    private readonly Dictionary<string, DateTimeOffset> _lastRuns = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_lastRuns.TryGetValue(jobKey, out var lastRun))
        if (!_options.Enabled)
        {
            logger.LogInformation("Scheduler hosted service is disabled by configuration.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            IReadOnlyList<ScheduledJobDefinition> enabledJobs;

            try
            {
                using var loadScope = serviceScopeFactory.CreateScope();
                var definitions = loadScope.ServiceProvider.GetRequiredService<IScheduledJobDefinitionProvider>();
                enabledJobs = await definitions.GetEnabledJobsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load scheduled jobs from persistence.");
                await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken).ConfigureAwait(false);
                continue;
            }

            foreach (var job in _options.Jobs.Where(static candidate => candidate.Enabled))
            {
                if (!CronScheduleEvaluator.IsDue(job.Cron, now))
                {
                    continue;
                }

                if (HasRunInCurrentMinute(job.Name, now))
                {
                    continue;
                }

                try
                {
                    using var scope = serviceScopeFactory.CreateScope();
                    var executor = scope.ServiceProvider.GetRequiredService<IScheduledJobExecutor>();
                    await executor.ExecuteAsync(job, stoppingToken).ConfigureAwait(false);
                    _lastRuns[GetLastRunKey(job)] = now;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Scheduled job {JobName} failed.", job.Name);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken).ConfigureAwait(false);
        }
    }

    private bool HasRunInCurrentMinute(string jobName, DateTimeOffset now)
    {
        if (!_lastRuns.TryGetValue(jobName, out var lastRun))
        {
            return false;
        }

        return lastRun.Year == now.Year
            && lastRun.Month == now.Month
            && lastRun.Day == now.Day
            && lastRun.Hour == now.Hour
            && lastRun.Minute == now.Minute;
    }

    private static string GetLastRunKey(ScheduledJobDefinition job)
    {
        if (job.Id != Guid.Empty)
        {
            return job.Id.ToString("N");
        }

        return $"{job.TenantId:N}:{job.ChannelId:N}:{job.Name}";
    }
}
                var jobKey = GetLastRunKey(job);
                if (HasRunInCurrentMinute(jobKey, now))
                    _lastRuns[jobKey] = now;
    private bool HasRunInCurrentMinute(string jobKey, DateTimeOffset now)
    /// <inheritdoc />
