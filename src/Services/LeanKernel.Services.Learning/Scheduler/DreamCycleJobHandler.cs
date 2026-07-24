using System.Collections.Concurrent;
using System.Text.Json;

using Cronos;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Services.Common.Interfaces;

using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Handles DreamCycle job execution with per-source-scope concurrency control.
/// </summary>
public sealed class DreamCycleJobHandler : IJobHandler
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ScopeLocks = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DreamCycleJobHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DreamCycleJobHandler"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="logger">The logger.</param>
    public DreamCycleJobHandler(IServiceScopeFactory scopeFactory, ILogger<DreamCycleJobHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(Guid jobId, string configurationJson, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dreamService = scope.ServiceProvider.GetRequiredService<IDreamService>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<EntityContext>>();

        var config = string.IsNullOrWhiteSpace(configurationJson)
            ? new DreamJobConfig(null, null, 0)
            : JsonSerializer.Deserialize<DreamJobConfig>(configurationJson) ?? new DreamJobConfig(null, null, 0);

        var sourceScope = config.SourceScope ?? "default";
        var mode = config.Mode ?? "full";
        var lockTimeout = TimeSpan.FromSeconds(config.LockTimeoutSeconds > 0 ? config.LockTimeoutSeconds : 300);

        var slim = ScopeLocks.GetOrAdd(sourceScope, _ => new SemaphoreSlim(1, 1));

        if (!await slim.WaitAsync(lockTimeout, ct))
        {
            await PersistSkippedRunAsync(contextFactory, sourceScope, mode, ct);
            await RescheduleWithJitterAsync(contextFactory, jobId, ct);
            _logger.LogWarning("Dream cycle for scope {Scope} skipped due to lock contention", sourceScope);
            return;
        }

        try
        {
            var runRecord = new DreamRunRecord
            {
                Id = Guid.NewGuid(),
                SourceScope = sourceScope,
                Mode = mode,
                StartedAt = DateTime.UtcNow,
                Status = "Running",
            };

            await using (var context = await contextFactory.CreateDbContextAsync(ct))
            {
                context.Set<DreamRunRecord>().Add(runRecord);
                await context.SaveChangesAsync(ct);
            }

            DreamRunResult result;
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeoutCts.CancelAfter(lockTimeout);

                try
                {
                    result = await dreamService.RunDreamAsync(sourceScope, mode, timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    await using var timeoutContext = await contextFactory.CreateDbContextAsync(CancellationToken.None);
                    var timeoutRecord = await timeoutContext.Set<DreamRunRecord>().FindAsync(new object[] { runRecord.Id }, CancellationToken.None);
                    if (timeoutRecord is not null)
                    {
                        timeoutRecord.Status = "TimedOut";
                        timeoutRecord.CompletedAt = DateTime.UtcNow;
                        await timeoutContext.SaveChangesAsync(CancellationToken.None);
                    }

                    await RescheduleWithJitterAsync(contextFactory, jobId, ct);
                    _logger.LogWarning("Dream cycle timed out for scope {Scope}", sourceScope);
                    return;
                }
            }

            await using (var context = await contextFactory.CreateDbContextAsync(ct))
            {
                var record = await context.Set<DreamRunRecord>().FindAsync(new object[] { runRecord.Id }, ct);
                if (record != null)
                {
                    record.Status = result.Status;
                    record.TotalPages = result.TotalPages;
                    record.FailedPages = result.FailedPages;
                    record.PhaseStatusJson = result.PhaseStatusJson;
                    record.CompletedAt = DateTime.UtcNow;
                    await context.SaveChangesAsync(ct);
                }
            }

            _logger.LogInformation("Dream cycle completed for scope {Scope}: {Status}", sourceScope, result.Status);
        }
        finally
        {
            slim.Release();
        }
    }

    private static async Task PersistSkippedRunAsync(
        IDbContextFactory<EntityContext> contextFactory,
        string sourceScope,
        string mode,
        CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        context.Set<DreamRunRecord>().Add(new DreamRunRecord
        {
            Id = Guid.NewGuid(),
            SourceScope = sourceScope,
            Mode = mode,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Status = "SkippedDueToLock",
        });

        await context.SaveChangesAsync(ct);
    }

    private static async Task RescheduleWithJitterAsync(
        IDbContextFactory<EntityContext> contextFactory,
        Guid jobId,
        CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var job = await context.Set<ScheduledJobEntity>().FindAsync(new object[] { jobId }, ct);
        if (job is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var next = now.AddMinutes(1);

        try
        {
            var cron = CronExpression.Parse(job.CronExpression, CronFormat.IncludeSeconds);
            var n1 = cron.GetNextOccurrence(now, TimeZoneInfo.Utc);
            var n2 = n1.HasValue ? cron.GetNextOccurrence(n1.Value, TimeZoneInfo.Utc) : null;

            if (n1.HasValue)
            {
                var interval = n2.HasValue ? n2.Value - n1.Value : TimeSpan.FromMinutes(1);
                var seconds = Math.Max(interval.TotalSeconds, 1);
                var jitterSeconds = seconds * 0.1;
                var centered = (Random.Shared.NextDouble() * 2.0) - 1.0;
                var random = centered * jitterSeconds;
                next = now.AddSeconds(seconds + random);
            }
        }
        catch (CronFormatException)
        {
            next = now.AddMinutes(1);
        }

        job.NextRunAt = next;
        job.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
    }

    private sealed record DreamJobConfig(string? SourceScope, string? Mode, int LockTimeoutSeconds);
}
