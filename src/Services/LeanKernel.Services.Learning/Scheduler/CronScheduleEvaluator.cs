using Cronos;

namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Evaluates cron expressions to determine job scheduling.
/// </summary>
public sealed class CronScheduleEvaluator
{
    /// <summary>
    /// Gets the next occurrence of the cron expression after the given time.
    /// </summary>
    /// <param name="cronExpression">The cron expression (with seconds field).</param>
    /// <param name="from">The time to compute the next occurrence from.</param>
    /// <returns>The next UTC occurrence, or <c>null</c> if the expression is invalid.</returns>
    public DateTime? GetNextOccurrence(string cronExpression, DateTime from)
    {
        try
        {
            var expression = CronExpression.Parse(cronExpression, CronFormat.IncludeSeconds);
            var next = expression.GetNextOccurrence(from.ToUniversalTime(), TimeZoneInfo.Utc);
            return next;
        }
        catch (CronFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Determines whether the cron expression is due at the given time.
    /// </summary>
    /// <param name="cronExpression">The cron expression (with seconds field).</param>
    /// <param name="now">The current time.</param>
    /// <returns><c>true</c> if the expression is due; otherwise <c>false</c>.</returns>
    public bool IsDue(string cronExpression, DateTime now)
    {
        var next = GetNextOccurrence(cronExpression, now.AddSeconds(-1));
        return next.HasValue && next.Value <= now;
    }
}