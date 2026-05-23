using System.Collections.Concurrent;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Scheduler;

/// <summary>
/// Runs the background scheduler loop for proactive jobs.
/// </summary>
public sealed class SchedulerHostedService(
    IServiceScopeFactory scopeFactory,
    IDbContextFactory<LeanKernelDbContext> dbFactory,
    CronScheduleEvaluator evaluator,
    IOptions<SchedulerConfig> config,
    TimeProvider timeProvider,
    ILogger<SchedulerHostedService> logger) : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly IDbContextFactory<LeanKernelDbContext> _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly CronScheduleEvaluator _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    private readonly SchedulerConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<SchedulerHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly ConcurrentDictionary<long, Task> _inFlight = [];
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastScheduledOccurrences = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _concurrencyGate = new(Math.Max(1, config.Value.MaxConcurrentJobs));
    private readonly TimeSpan _tickInterval = TimeSpan.FromSeconds(Math.Max(1, config.Value.TickIntervalSeconds));
    private Task? _runTask;
    private long _workItemId;
    private volatile bool _acceptingWork = true;

    /// <summary>
    /// Gets the number of in-flight scheduled jobs.
    /// </summary>
    public int InFlightCount => _inFlight.Count;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("Scheduler hosted service is disabled via configuration");
            return Task.CompletedTask;
        }

        _acceptingWork = true;
        _runTask ??= RunAsync(_shutdownCts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct)
    {
        if (_runTask is null)
        {
            return;
        }

        _acceptingWork = false;
        _shutdownCts.Cancel();

        try
        {
            await _runTask.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Scheduler shutdown timed out with {InFlightCount} jobs still running",
                _inFlight.Count);
        }
    }

    /// <summary>
    /// Waits for all in-flight jobs to complete.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when all in-flight jobs are done.</returns>
    public async Task AwaitInFlightAsync(CancellationToken ct = default)
    {
        while (_inFlight.Count > 0)
        {
            var tasks = _inFlight.Values.ToArray();
            await Task.WhenAll(tasks).WaitAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Processes a single scheduler tick at the supplied UTC timestamp.
    /// </summary>
    /// <param name="nowUtc">The UTC timestamp representing the current tick time.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the tick has been evaluated.</returns>
    public async Task ProcessTickAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        if (!_acceptingWork)
        {
            return;
        }

        foreach (var job in _config.Jobs.Where(candidate => candidate.Enabled))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var lastScheduledAt = await GetLastScheduledOccurrenceAsync(job.Name, ct).ConfigureAwait(false);
                if (!_evaluator.IsDue(job, nowUtc, lastScheduledAt, out var scheduledAt) || scheduledAt is null)
                {
                    continue;
                }

                await _concurrencyGate.WaitAsync(ct).ConfigureAwait(false);
                if (!_acceptingWork)
                {
                    _concurrencyGate.Release();
                    return;
                }

                if (!TryReserveOccurrence(job.Name, scheduledAt.Value))
                {
                    _concurrencyGate.Release();
                    continue;
                }

                var workItemId = Interlocked.Increment(ref _workItemId);
                var task = ExecuteJobAsync(workItemId, job, scheduledAt.Value);
                _inFlight[workItemId] = task;
                _ = ObserveCompletionAsync(workItemId, task);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to evaluate scheduled job {JobName}", job.Name);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _shutdownCts.Dispose();
        _concurrencyGate.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await ProcessTickAsync(_timeProvider.GetUtcNow(), ct).ConfigureAwait(false);
                await Task.Delay(_tickInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Scheduler loop cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduler loop terminated unexpectedly");
        }
        finally
        {
            _acceptingWork = false;
            await AwaitInFlightAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task ExecuteJobAsync(long workItemId, ScheduledJobDefinition job, DateTimeOffset scheduledAt)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();
            var execution = await executor.ExecuteAsync(job, scheduledAt).ConfigureAwait(false);

            if (execution.Success)
            {
                _logger.LogInformation(
                    "Scheduled job {JobName} completed successfully in {DurationMs}ms",
                    job.Name,
                    execution.Duration.TotalMilliseconds);
            }
            else
            {
                _logger.LogWarning(
                    "Scheduled job {JobName} failed after {DurationMs}ms: {Error}",
                    job.Name,
                    execution.Duration.TotalMilliseconds,
                    execution.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Scheduled job {JobName} failed unexpectedly while processing work item {WorkItemId}",
                job.Name,
                workItemId);
        }
    }

    private async Task ObserveCompletionAsync(long workItemId, Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(workItemId, out _);
            _concurrencyGate.Release();
        }
    }

    private async Task<DateTimeOffset?> GetLastScheduledOccurrenceAsync(string jobName, CancellationToken ct)
    {
        if (_lastScheduledOccurrences.TryGetValue(jobName, out var cachedScheduledAt))
        {
            return cachedScheduledAt;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var persistedScheduledAt = await db.ScheduledJobExecutions
            .AsNoTracking()
            .Where(entry => entry.JobName == jobName)
            .OrderByDescending(entry => entry.ScheduledAt)
            .Select(entry => (DateTimeOffset?)entry.ScheduledAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (persistedScheduledAt is not null)
        {
            _lastScheduledOccurrences[jobName] = persistedScheduledAt.Value;
        }

        return persistedScheduledAt;
    }

    private bool TryReserveOccurrence(string jobName, DateTimeOffset scheduledAt)
    {
        while (true)
        {
            if (_lastScheduledOccurrences.TryGetValue(jobName, out var existingScheduledAt))
            {
                if (existingScheduledAt >= scheduledAt)
                {
                    return false;
                }

                if (_lastScheduledOccurrences.TryUpdate(jobName, scheduledAt, existingScheduledAt))
                {
                    return true;
                }

                continue;
            }

            if (_lastScheduledOccurrences.TryAdd(jobName, scheduledAt))
            {
                return true;
            }
        }
    }
}
