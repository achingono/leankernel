using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Diagnostics.SpendGuard;

/// <summary>
/// Tracks node-local spend totals and optionally persists snapshots through diagnostics.
/// </summary>
public sealed class SpendTracker(
    LeanKernelMetrics metrics,
    ILogger<SpendTracker> logger,
    TimeProvider? timeProvider = null,
    IDiagnosticsSink? diagnosticsSink = null) : ISpendTracker
{
    private readonly LeanKernelMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    private readonly ILogger<SpendTracker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IDiagnosticsSink? _diagnosticsSink = diagnosticsSink;
    private readonly object _sync = new();
    private readonly Dictionary<string, decimal> _sessionTotalsUsd = new(StringComparer.OrdinalIgnoreCase);
    private DateOnly _currentDayUtc = DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);
    private DateOnly _currentMonthUtc;
    private decimal _dailyTotalUsd;
    private decimal _monthlyTotalUsd;

    /// <inheritdoc />
    public SpendSnapshot GetSnapshot(DateTimeOffset? asOf = null)
    {
        lock (_sync)
        {
            RollBoundaries(asOf ?? _timeProvider.GetUtcNow());
            return CreateSnapshotLocked();
        }
    }

    /// <inheritdoc />
    public async Task<SpendSnapshot> RecordSpendAsync(string sessionId, string turnId, decimal amountUsd, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);

        SpendSnapshot snapshot;
        decimal sessionTotalUsd;
        lock (_sync)
        {
            RollBoundaries(_timeProvider.GetUtcNow());
            var normalizedAmount = Math.Max(0m, amountUsd);
            _dailyTotalUsd += normalizedAmount;
            _monthlyTotalUsd += normalizedAmount;
            _sessionTotalsUsd[sessionId] = _sessionTotalsUsd.TryGetValue(sessionId, out var currentValue)
                ? currentValue + normalizedAmount
                : normalizedAmount;
            sessionTotalUsd = _sessionTotalsUsd[sessionId];
            snapshot = CreateSnapshotLocked();
            _metrics.SetSpendTotals(snapshot.DailyTotalUsd, snapshot.MonthlyTotalUsd);
        }

        if (_diagnosticsSink is not null)
        {
            try
            {
                await _diagnosticsSink.RecordAsync(new DiagnosticEntry
                {
                    SessionId = sessionId,
                    TurnId = turnId,
                    Category = "SpendTracking",
                    Payload = new
                    {
                        amountUsd = Math.Max(0m, amountUsd),
                        dailyTotalUsd = snapshot.DailyTotalUsd,
                        sessionTotalUsd,
                        monthlyTotalUsd = snapshot.MonthlyTotalUsd,
                    }
                }, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Spend-tracking diagnostic persistence failed for session {SessionId}", sessionId);
            }
        }

        return snapshot;
    }

    private SpendSnapshot CreateSnapshotLocked()
        => new()
        {
            DayUtc = _currentDayUtc,
            MonthUtc = _currentMonthUtc,
            DailyTotalUsd = _dailyTotalUsd,
            MonthlyTotalUsd = _monthlyTotalUsd,
            SessionTotalsUsd = new Dictionary<string, decimal>(_sessionTotalsUsd, StringComparer.OrdinalIgnoreCase),
        };

    private void RollBoundaries(DateTimeOffset now)
    {
        var utcDate = DateOnly.FromDateTime(now.UtcDateTime);
        if (utcDate != _currentDayUtc)
        {
            _currentDayUtc = utcDate;
            _dailyTotalUsd = 0m;
        }

        var monthBoundary = new DateOnly(utcDate.Year, utcDate.Month, 1);
        if (monthBoundary != _currentMonthUtc)
        {
            _currentMonthUtc = monthBoundary;
            _monthlyTotalUsd = 0m;
        }
    }
}
