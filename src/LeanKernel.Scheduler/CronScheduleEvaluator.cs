using System.Collections.Concurrent;
using Cronos;
using LeanKernel.Abstractions.Configuration;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Scheduler;

/// <summary>
/// Evaluates cron schedules for configured jobs.
/// </summary>
public sealed class CronScheduleEvaluator(
    TimeBoundaryService timeBoundaryService,
    ILogger<CronScheduleEvaluator> logger)
{
    private static readonly TimeSpan[] InitialLookbacks =
    [
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(31),
        TimeSpan.FromDays(366),
        TimeSpan.FromDays(366 * 5),
    ];

    private readonly TimeBoundaryService _timeBoundaryService = timeBoundaryService ?? throw new ArgumentNullException(nameof(timeBoundaryService));
    private readonly ILogger<CronScheduleEvaluator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<string, CronExpression> _expressionCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the next scheduled occurrence for a job after the supplied UTC timestamp.
    /// </summary>
    /// <param name="job">The job definition.</param>
    /// <param name="fromUtc">The UTC timestamp after which to search.</param>
    /// <returns>The next scheduled occurrence, when one exists.</returns>
    public DateTimeOffset? GetNextOccurrence(ScheduledJobDefinition job, DateTimeOffset fromUtc)
    {
        ArgumentNullException.ThrowIfNull(job);

        var expression = GetExpression(job.CronExpression);
        var timeZone = _timeBoundaryService.ResolveTimeZone(GetTimeZoneId(job));
        return expression.GetNextOccurrence(fromUtc, timeZone, inclusive: false);
    }

    /// <summary>
    /// Determines whether a job is due at the supplied UTC timestamp.
    /// </summary>
    /// <param name="job">The job definition.</param>
    /// <param name="nowUtc">The UTC timestamp to evaluate.</param>
    /// <param name="lastScheduledAt">The last scheduled occurrence that already executed.</param>
    /// <param name="scheduledAt">The scheduled occurrence that is now due.</param>
    /// <returns><see langword="true"/> when the job should execute; otherwise <see langword="false"/>.</returns>
    public bool IsDue(
        ScheduledJobDefinition job,
        DateTimeOffset nowUtc,
        DateTimeOffset? lastScheduledAt,
        out DateTimeOffset? scheduledAt)
    {
        ArgumentNullException.ThrowIfNull(job);

        scheduledAt = null;

        if (lastScheduledAt is { } lastExecution)
        {
            var nextOccurrence = GetNextOccurrence(job, lastExecution);
            if (nextOccurrence is null || nextOccurrence > nowUtc)
            {
                return false;
            }

            scheduledAt = nextOccurrence;
            return true;
        }

        var expression = GetExpression(job.CronExpression);
        var timeZone = _timeBoundaryService.ResolveTimeZone(GetTimeZoneId(job));

        foreach (var lookback in InitialLookbacks)
        {
            var latestOccurrence = GetLatestOccurrence(expression, nowUtc - lookback, nowUtc, timeZone);
            if (latestOccurrence is null)
            {
                continue;
            }

            scheduledAt = latestOccurrence;
            return true;
        }

        _logger.LogDebug(
            "No due occurrence found for scheduled job {JobName} at {NowUtc}",
            job.Name,
            nowUtc);
        return false;
    }

    private CronExpression GetExpression(string cronExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);

        return _expressionCache.GetOrAdd(
            cronExpression,
            static expression => CronExpression.Parse(expression, CronFormat.Standard));
    }

    private static DateTimeOffset? GetLatestOccurrence(
        CronExpression expression,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        TimeZoneInfo timeZone)
    {
        DateTimeOffset? latestOccurrence = null;

        foreach (var occurrence in expression.GetOccurrences(
                     fromUtc,
                     toUtc,
                     timeZone,
                     fromInclusive: true,
                     toInclusive: true))
        {
            latestOccurrence = occurrence;
        }

        return latestOccurrence;
    }

    private static string? GetTimeZoneId(ScheduledJobDefinition job)
        => job.Parameters.TryGetValue("timezone", out var timeZoneId) && !string.IsNullOrWhiteSpace(timeZoneId)
            ? timeZoneId
            : null;
}
