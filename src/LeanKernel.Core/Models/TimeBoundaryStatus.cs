namespace LeanKernel.Core.Models;

/// <summary>
/// Current time-boundary status for diagnostics and queueing.
/// </summary>
public sealed class TimeBoundaryStatus
{
    /// <summary>Gets whether the current time is within active hours.</summary>
    public required bool IsInActiveHours { get; init; }

    /// <summary>Gets the next active window in UTC.</summary>
    public required DateTime NextActiveWindow { get; init; }

    /// <summary>Gets whether the current time is the configured Sabbath.</summary>
    public required bool IsSabbath { get; init; }

    /// <summary>Gets whether the current time is quiet hours.</summary>
    public required bool IsQuietHours { get; init; }

    /// <summary>Gets the current time zone identifier.</summary>
    public required string CurrentTimeZone { get; init; }
}
