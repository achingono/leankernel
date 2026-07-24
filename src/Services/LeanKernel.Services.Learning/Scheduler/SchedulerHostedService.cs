using LeanKernel.Data;
using LeanKernel.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Background service that evaluates cron schedules and executes due jobs.
/// </summary>
public sealed class SchedulerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<SchedulerSettings> _settings;
    private readonly ILogger<SchedulerHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SchedulerHostedService"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="settings">The scheduler settings.</param>
    /// <param name="logger">The logger.</param>
    public SchedulerHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<SchedulerSettings> settings,
        ILogger<SchedulerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduler hosted service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateAndExecuteJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in scheduler loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.Value.PollIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Scheduler hosted service stopped");
    }

    private async Task EvaluateAndExecuteJobsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EntityContext>();
        var evaluator = scope.ServiceProvider.GetRequiredService<CronScheduleEvaluator>();
        var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();

        var now = DateTime.UtcNow;
        var dueJobs = await context.Set<ScheduledJobEntity>()
            .Where(j => j.Enabled && j.NextRunAt <= now)
            .ToListAsync(ct);

        foreach (var job in dueJobs)
        {
            await executor.ExecuteAsync(job, ct);
            job.LastRunAt = now;

            var nextRun = evaluator.GetNextOccurrence(job.CronExpression, now);
            job.NextRunAt = nextRun ?? now.AddDays(1);
            job.UpdatedAt = now;
        }

        if (dueJobs.Count > 0)
        {
            await context.SaveChangesAsync(ct);
            _logger.LogInformation("Executed {Count} scheduled jobs", dueJobs.Count);
        }
    }
}