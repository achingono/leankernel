using LeanKernel.Abstractions.Configuration;
using Microsoft.Extensions.Options;

namespace LeanKernel.Scheduler;

/// <summary>
/// Provides timezone-aware day-boundary calculations for scheduled jobs.
/// </summary>
public sealed class TimeBoundaryService(IOptions<SchedulerConfig> config)
{
    private readonly SchedulerConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;

    /// <summary>
    /// Gets the current local day boundary for the supplied UTC timestamp.
    /// </summary>
    /// <param name="utcNow">The UTC timestamp to evaluate.</param>
    /// <param name="timeZoneId">The optional timezone identifier.</param>
    /// <returns>The resolved time boundary.</returns>
    public TimeBoundary GetCurrentBoundary(DateTimeOffset utcNow, string? timeZoneId = null)
    {
        var localTime = TimeZoneInfo.ConvertTime(utcNow, ResolveTimeZone(timeZoneId));
        return localTime.Hour switch
        {
            < 6 => TimeBoundary.Night,
            < 12 => TimeBoundary.Morning,
            < 18 => TimeBoundary.Afternoon,
            < 22 => TimeBoundary.Evening,
            _ => TimeBoundary.Night,
        };
    }

    /// <summary>
    /// Gets the UTC start time for the current boundary.
    /// </summary>
    /// <param name="utcNow">The UTC timestamp to evaluate.</param>
    /// <param name="timeZoneId">The optional timezone identifier.</param>
    /// <returns>The UTC timestamp for the current boundary start.</returns>
    public DateTimeOffset GetStartOfCurrentBoundary(DateTimeOffset utcNow, string? timeZoneId = null)
        => GetBoundaryStart(utcNow, GetCurrentBoundary(utcNow, timeZoneId), timeZoneId);

    /// <summary>
    /// Gets the UTC start time for a specific boundary on the day containing the supplied timestamp.
    /// </summary>
    /// <param name="utcNow">The UTC timestamp to evaluate.</param>
    /// <param name="boundary">The boundary whose start should be returned.</param>
    /// <param name="timeZoneId">The optional timezone identifier.</param>
    /// <returns>The UTC timestamp for the requested boundary start.</returns>
    public DateTimeOffset GetBoundaryStart(DateTimeOffset utcNow, TimeBoundary boundary, string? timeZoneId = null)
    {
        var timeZone = ResolveTimeZone(timeZoneId);
        var localTime = TimeZoneInfo.ConvertTime(utcNow, timeZone);
        var localDate = localTime.Date;

        var localBoundary = boundary switch
        {
            TimeBoundary.Morning => localDate.AddHours(6),
            TimeBoundary.Afternoon => localDate.AddHours(12),
            TimeBoundary.Evening => localDate.AddHours(18),
            TimeBoundary.Night when localTime.Hour < 6 => localDate.AddDays(-1).AddHours(22),
            TimeBoundary.Night => localDate.AddHours(22),
            _ => throw new ArgumentOutOfRangeException(nameof(boundary), boundary, "Unsupported time boundary."),
        };

        return ConvertLocalTimeToUtc(localBoundary, timeZone);
    }

    /// <summary>
    /// Resolves a timezone identifier, falling back to the configured scheduler default.
    /// </summary>
    /// <param name="timeZoneId">The optional timezone identifier.</param>
    /// <returns>The resolved timezone.</returns>
    public TimeZoneInfo ResolveTimeZone(string? timeZoneId = null)
    {
        var effectiveTimeZoneId = string.IsNullOrWhiteSpace(timeZoneId)
            ? _config.DefaultTimezone
            : timeZoneId;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(effectiveTimeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new ArgumentException($"Timezone '{effectiveTimeZoneId}' was not found.", nameof(timeZoneId), ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new ArgumentException($"Timezone '{effectiveTimeZoneId}' is invalid.", nameof(timeZoneId), ex);
        }
    }

    private static DateTimeOffset ConvertLocalTimeToUtc(DateTime localTime, TimeZoneInfo timeZone)
    {
        var unspecified = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        var offset = timeZone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }
}
