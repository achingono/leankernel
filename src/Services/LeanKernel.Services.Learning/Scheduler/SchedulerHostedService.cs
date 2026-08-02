using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Services.Common.HealthChecks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Background service that evaluates cron schedules and executes due jobs
/// using atomic lease claims to prevent duplicate execution across instances.
/// </summary>
public sealed class SchedulerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<SchedulerSettings> _settings;
    private readonly ILogger<SchedulerHostedService> _logger;
    private readonly WorkerHealthState? _healthState;
    private readonly string _workerId;

    /// <summary>
    /// Initializes a new instance of the <see cref="SchedulerHostedService"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="settings">The scheduler settings.</param>
    /// <param name="healthState">Optional worker health state tracker.</param>
    /// <param name="logger">The logger.</param>
    public SchedulerHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<SchedulerSettings> settings,
        WorkerHealthState? healthState = null,
        ILogger<SchedulerHostedService> logger = null!)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _healthState = healthState;
        _logger = logger;
        _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduler hosted service started (worker {WorkerId})", _workerId);

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
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<EntityContext>>();
        var evaluator = scope.ServiceProvider.GetRequiredService<CronScheduleEvaluator>();
        var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();

        var now = DateTime.UtcNow;
        var leaseDuration = TimeSpan.FromSeconds(
            _settings.Value.DreamLockTimeoutSeconds > 0
                ? _settings.Value.DreamLockTimeoutSeconds
                : 300);

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var dueJobs = await context.Set<ScheduledJobEntity>()
            .Where(j => j.Enabled && j.NextRunAt <= now)
            .ToListAsync(ct);

        foreach (var job in dueJobs)
        {
            var leaseExpiry = now + leaseDuration;
            var claimed = await context.Database.ExecuteSqlRawAsync(
                """
                UPDATE "ScheduledJobs"
                SET "LeaseOwner" = {0},
                    "LeaseExpiresAt" = {1},
                    "UpdatedAt" = {2}
                WHERE "Id" = {3}
                  AND "NextRunAt" <= {4}
                  AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= {5})
                """,
                new object[] { _workerId, leaseExpiry, now, job.Id, now, now }, ct);

            if (claimed == 0)
            {
                continue;
            }

            try
            {
                await executor.ExecuteAsync(job, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled job {JobName} ({JobId}) failed", job.Name, job.Id);
            }

            job.LastRunAt = now;
            var nextRun = evaluator.GetNextOccurrence(job.CronExpression, now);
            job.NextRunAt = nextRun ?? now.AddDays(1);
            job.UpdatedAt = now;
            job.LeaseExpiresAt = null;
            job.LeaseOwner = null;
        }

        if (dueJobs.Count > 0)
        {
            await context.SaveChangesAsync(ct);
            _logger.LogInformation("Evaluated and updated {Count} scheduled jobs", dueJobs.Count);
        }

        _healthState?.MarkSchedulerHealthy();
    }
}