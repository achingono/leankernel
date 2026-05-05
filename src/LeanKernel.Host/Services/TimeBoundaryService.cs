using Microsoft.Extensions.Logging;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Host.Services;

/// <summary>
/// Manages time-based engagement boundaries (active hours, quiet hours, Sabbath).
/// </summary>
public interface ITimeBoundaryService
{
    /// <summary>
    /// Check if current time is within active hours.
    /// </summary>
    bool IsInActiveHours();
    
    /// <summary>
    /// Get the next active window start time.
    /// </summary>
    DateTime GetNextActiveWindow();
    
    /// <summary>
    /// Get current status (for diagnostics).
    /// </summary>
    TimeBoundaryStatus GetStatus();
}

/// <summary>
/// Current time boundary status.
/// </summary>
public sealed class TimeBoundaryStatus
{
    public required bool IsInActiveHours { get; init; }
    public required DateTime NextActiveWindow { get; init; }
    public required bool IsSabbath { get; init; }
    public required bool IsQuietHours { get; init; }
    public required string CurrentTimeZone { get; init; }
}

/// <summary>
/// Service for checking time boundaries from engagement rules.
/// </summary>
public sealed class TimeBoundaryService : ITimeBoundaryService
{
    private readonly EngagementRules _rules;
    private readonly TimeZoneInfo _userTimeZone;
    private readonly ILogger<TimeBoundaryService> _logger;

    public TimeBoundaryService(EngagementRules rules, ILogger<TimeBoundaryService> logger)
    {
        _rules = rules;
        _logger = logger;
        
        // Parse timezone from engagement rules (default to UTC if not specified or invalid)
        var tzName = _rules.TimeBoundaries.Timezone ?? "UTC";
        
        // Map common timezone names to IANA timezone identifiers
        tzName = tzName switch
        {
            "Eastern" => "America/New_York",
            "Central" => "America/Chicago",
            "Mountain" => "America/Denver",
            "Pacific" => "America/Los_Angeles",
            "GMT" => "Etc/UTC",
            "UTC" => "Etc/UTC",
            _ => tzName
        };
        
        try
        {
            _userTimeZone = TimeZoneInfo.FindSystemTimeZoneById(tzName);
        }
        catch (TimeZoneNotFoundException)
        {
            _logger.LogWarning("Timezone {Timezone} not found; using UTC", tzName);
            _userTimeZone = TimeZoneInfo.Utc;
        }
    }

    public bool IsInActiveHours()
    {
        var now = TimeZoneInfo.ConvertTime(DateTime.UtcNow, _userTimeZone);
        var boundaries = _rules.TimeBoundaries;
        
        // Check Sabbath first
        if (boundaries.SabbathDay.HasValue && now.DayOfWeek == boundaries.SabbathDay && !boundaries.AllowSabbathMessages)
        {
            _logger.LogDebug("Current time is Sabbath; not in active hours");
            return false;
        }
        
        // Check active hours boundaries
        if (boundaries.ActiveHoursStart.HasValue && now.Hour < boundaries.ActiveHoursStart.Value)
        {
            _logger.LogDebug("Current time {Hour}:00 is before active hours start {Start}", now.Hour, boundaries.ActiveHoursStart);
            return false;
        }
        
        if (boundaries.ActiveHoursEnd.HasValue && now.Hour >= boundaries.ActiveHoursEnd.Value)
        {
            _logger.LogDebug("Current time {Hour}:00 is after active hours end {End}", now.Hour, boundaries.ActiveHoursEnd);
            return false;
        }
        
        _logger.LogDebug("Current time is within active hours");
        return true;
    }

    public DateTime GetNextActiveWindow()
    {
        var now = TimeZoneInfo.ConvertTime(DateTime.UtcNow, _userTimeZone);
        var boundaries = _rules.TimeBoundaries;
        
        // Start with tomorrow at active hours start
        var hour = boundaries.ActiveHoursStart ?? 8;
        var next = now.Date.AddDays(1).AddHours(hour);
        
        // If Sabbath, skip to next day after Sabbath
        if (boundaries.SabbathDay.HasValue)
        {
            while (next.DayOfWeek == boundaries.SabbathDay.Value)
            {
                next = next.AddDays(1);
            }
        }
        
        // Convert back to UTC for storage
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(next, _userTimeZone);
        
        _logger.LogDebug("Next active window: {NextWindow}", nextUtc);
        return nextUtc;
    }

    public TimeBoundaryStatus GetStatus()
    {
        var now = TimeZoneInfo.ConvertTime(DateTime.UtcNow, _userTimeZone);
        var boundaries = _rules.TimeBoundaries;
        var isActive = IsInActiveHours();
        var nextWindow = GetNextActiveWindow();
        
        var isSabbath = boundaries.SabbathDay.HasValue && now.DayOfWeek == boundaries.SabbathDay;
        var isQuiet = !isSabbath && 
            (boundaries.ActiveHoursStart.HasValue && now.Hour < boundaries.ActiveHoursStart.Value ||
             boundaries.ActiveHoursEnd.HasValue && now.Hour >= boundaries.ActiveHoursEnd.Value);
        
        return new TimeBoundaryStatus
        {
            IsInActiveHours = isActive,
            NextActiveWindow = nextWindow,
            IsSabbath = isSabbath,
            IsQuietHours = isQuiet,
            CurrentTimeZone = _userTimeZone.Id
        };
    }
}
