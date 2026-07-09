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
    private readonly Dictionary<string, decimal> _reservedSessionTotalsUsd = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, SpendReservation> _reservations = new();
    private DateOnly _currentDayUtc = DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);
    private DateOnly _currentMonthUtc;
    private decimal _dailyTotalUsd;
    private decimal _monthlyTotalUsd;
    private decimal _reservedDailyTotalUsd;
    private decimal _reservedMonthlyTotalUsd;

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
    public bool TryReserveSpend(
        string sessionId,
        string turnId,
        decimal amountUsd,
        decimal maxDailySpendUsd,
        decimal maxSessionSpendUsd,
        decimal maxMonthlySpendUsd,
        out SpendReservation? reservation,
        DateTimeOffset? asOf = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);

        lock (_sync)
        {
            RollBoundaries(asOf ?? _timeProvider.GetUtcNow());

            var normalizedAmount = Math.Max(0m, amountUsd);
            var projectedDaily = _dailyTotalUsd + _reservedDailyTotalUsd + normalizedAmount;
            var projectedMonthly = _monthlyTotalUsd + _reservedMonthlyTotalUsd + normalizedAmount;
            var projectedSession = (_sessionTotalsUsd.TryGetValue(sessionId, out var sessionTotal) ? sessionTotal : 0m)
                + (_reservedSessionTotalsUsd.TryGetValue(sessionId, out var reservedSessionTotal) ? reservedSessionTotal : 0m)
                + normalizedAmount;

            if ((maxDailySpendUsd > 0m && projectedDaily > maxDailySpendUsd)
                || (maxMonthlySpendUsd > 0m && projectedMonthly > maxMonthlySpendUsd)
                || (maxSessionSpendUsd > 0m && projectedSession > maxSessionSpendUsd))
            {
                reservation = null;
                return false;
            }

            reservation = new SpendReservation
            {
                ReservationId = Guid.NewGuid(),
                SessionId = sessionId,
                TurnId = turnId,
                ReservedAmountUsd = normalizedAmount,
            };

            _reservations[reservation.ReservationId] = reservation;
            _reservedDailyTotalUsd += normalizedAmount;
            _reservedMonthlyTotalUsd += normalizedAmount;
            _reservedSessionTotalsUsd[sessionId] = _reservedSessionTotalsUsd.TryGetValue(sessionId, out var currentReserved)
                ? currentReserved + normalizedAmount
                : normalizedAmount;
            return true;
        }
    }

    /// <inheritdoc />
    public async Task<SpendSnapshot> CommitReservedSpendAsync(SpendReservation reservation, decimal? actualAmountUsd = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        ReleaseReservationInternal(reservation, out var knownReservation);
        if (!knownReservation)
        {
            _logger.LogWarning("Spend reservation {ReservationId} was already released or committed", reservation.ReservationId);
            return GetSnapshot();
        }

        var amountToCommit = actualAmountUsd ?? reservation.ReservedAmountUsd;
        return await RecordSpendAsync(reservation.SessionId, reservation.TurnId, amountToCommit, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void ReleaseReservedSpend(SpendReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ReleaseReservationInternal(reservation, out _);
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
            _reservedDailyTotalUsd = 0m;
            _reservedSessionTotalsUsd.Clear();
            _reservations.Clear();
        }

        var monthBoundary = new DateOnly(utcDate.Year, utcDate.Month, 1);
        if (monthBoundary != _currentMonthUtc)
        {
            _currentMonthUtc = monthBoundary;
            _monthlyTotalUsd = 0m;
            _reservedMonthlyTotalUsd = 0m;
            _reservedSessionTotalsUsd.Clear();
            _reservations.Clear();
        }
    }

    private void ReleaseReservationInternal(SpendReservation reservation, out bool knownReservation)
    {
        lock (_sync)
        {
            if (!_reservations.Remove(reservation.ReservationId, out var existing))
            {
                knownReservation = false;
                return;
            }

            knownReservation = true;
            var amount = existing.ReservedAmountUsd;
            _reservedDailyTotalUsd = Math.Max(0m, _reservedDailyTotalUsd - amount);
            _reservedMonthlyTotalUsd = Math.Max(0m, _reservedMonthlyTotalUsd - amount);

            if (_reservedSessionTotalsUsd.TryGetValue(existing.SessionId, out var reservedSession))
            {
                var updated = reservedSession - amount;
                if (updated <= 0m)
                {
                    _reservedSessionTotalsUsd.Remove(existing.SessionId);
                }
                else
                {
                    _reservedSessionTotalsUsd[existing.SessionId] = updated;
                }
            }
        }
    }
}
