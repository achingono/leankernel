namespace LeanKernel.Logic.Telemetry.Models;

/// <summary>
/// Inclusive telemetry query range.
/// </summary>
public readonly record struct DateRange(DateTimeOffset From, DateTimeOffset To)
{
    /// <summary>
    /// Gets a value indicating whether the range is valid.
    /// </summary>
    public bool IsValid => From <= To;

    /// <summary>
    /// Returns the last 7 days in UTC.
    /// </summary>
    public static DateRange Last7Days() => new(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow);

    /// <summary>
    /// Returns the last 30 days in UTC.
    /// </summary>
    public static DateRange Last30Days() => new(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow);

    /// <summary>
    /// Returns the current UTC month range.
    /// </summary>
    public static DateRange CurrentMonth()
    {
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return new DateRange(monthStart, now);
    }
}