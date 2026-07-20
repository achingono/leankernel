using Cronos;

namespace LeanKernel.Services.Common.Scheduler;

/// <summary>
/// Evaluates cron schedules against UTC minute boundaries.
/// </summary>
public static class CronScheduleEvaluator
{
    /// <summary>
    /// Returns true when the cron expression is due for the provided UTC time.
    /// </summary>
    public static bool IsDue(string cron, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            return false;
        }

        CronExpression expression;
        try
        {
            expression = CronExpression.Parse(cron, CronFormat.Standard);
        }
        catch (CronFormatException)
        {
            return false;
        }

        var minuteStartUtc = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, utcNow.Minute, 0, DateTimeKind.Utc);
        var windowStartUtc = minuteStartUtc.AddSeconds(-1);
        var next = expression.GetNextOccurrence(windowStartUtc, TimeZoneInfo.Utc);
        return next.HasValue && next.Value <= minuteStartUtc;
    }
}
