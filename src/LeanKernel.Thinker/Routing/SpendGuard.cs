using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Thinker.Routing;

/// <summary>
/// Tracks daily paid-request usage and enforces soft/hard spend thresholds (FR-8).
/// </summary>
public sealed class SpendGuard
{
    private readonly RoutingConfig _config;
    private readonly ILogger<SpendGuard> _logger;

    // Thread-safe daily counters.
    private int _dailyPaidRequestCount;
    private DateOnly _currentDay;
    private readonly object _lock = new();

    public SpendGuard(IOptions<LeanKernelConfig> config, ILogger<SpendGuard> logger)
    {
        _config = config.Value.Routing;
        _logger = logger;
        _currentDay = DateOnly.FromDateTime(DateTime.UtcNow);
    }

    /// <summary>
    /// Returns <c>true</c> when the hard paid-request limit is active, meaning
    /// paid fallback should be skipped for non-critical requests.
    /// </summary>
    public bool ILeanKernelLimitActive()
    {
        if (_config.SpendGuard.DailyPaidRequestHardLimit <= 0)
            return false;

        return CurrentCount() >= _config.SpendGuard.DailyPaidRequestHardLimit;
    }

    /// <summary>
    /// Records a paid request. Emits warnings/critical logs at threshold crossings.
    /// </summary>
    public void RecordPaidRequest()
    {
        lock (_lock)
        {
            RollDayIfNeeded();
            _dailyPaidRequestCount++;
            var count = _dailyPaidRequestCount;

            var soft = _config.SpendGuard.DailyPaidRequestSoftLimit;
            var hard = _config.SpendGuard.DailyPaidRequestHardLimit;

            if (hard > 0 && count >= hard)
            {
                _logger.LogCritical(
                    "SpendGuard: daily paid-request HARD limit reached ({Count}/{Hard}). " +
                    "Paid fallback disabled for remainder of UTC day.",
                    count, hard);
            }
            else if (soft > 0 && count >= soft)
            {
                _logger.LogWarning(
                    "SpendGuard: daily paid-request SOFT limit reached ({Count}/{Soft}).",
                    count, soft);
            }
        }
    }

    /// <summary>
    /// Returns the current paid-request count for today.
    /// </summary>
    public int CurrentCount()
    {
        lock (_lock)
        {
            RollDayIfNeeded();
            return _dailyPaidRequestCount;
        }
    }

    private void RollDayIfNeeded()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (today != _currentDay)
        {
            _currentDay = today;
            _dailyPaidRequestCount = 0;
        }
    }
}
