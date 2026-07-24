namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Evaluates time-based windows for scheduled job execution.
/// </summary>
public sealed class TimeBoundaryService
{
    /// <summary>
    /// Determines whether the given time falls within the specified window.
    /// Supports overnight windows (e.g. 22:00 to 06:00).
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="start">The start of the window.</param>
    /// <param name="end">The end of the window.</param>
    /// <returns><c>true</c> if the time is within the window; otherwise <c>false</c>.</returns>
    public bool IsWithinWindow(DateTimeOffset now, TimeSpan start, TimeSpan end)
    {
        var timeOfDay = now.TimeOfDay;
        return start <= end
            ? timeOfDay >= start && timeOfDay <= end
            : timeOfDay >= start || timeOfDay <= end;
    }

    /// <summary>
    /// Computes the next UTC occurrence of the given start time.
    /// </summary>
    /// <param name="start">The start time of the window.</param>
    /// <returns>The next occurrence of the start time.</returns>
    public DateTimeOffset NextWindowStart(TimeSpan start)
    {
        var now = DateTimeOffset.UtcNow;
        var candidate = now.Date + start;
        return candidate > now ? candidate : candidate.AddDays(1);
    }
}