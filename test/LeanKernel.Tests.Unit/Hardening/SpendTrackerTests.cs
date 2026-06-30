using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Diagnostics;
using LeanKernel.Diagnostics.SpendGuard;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeanKernel.Tests.Unit.Hardening;

public class SpendTrackerTests
{
    [Fact]
    public void GetSnapshot_returns_initial_zero_totals()
    {
        using var metrics = new LeanKernelMetrics();
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, TimeProvider.System);

        var snapshot = tracker.GetSnapshot();

        snapshot.DailyTotalUsd.Should().Be(0m);
        snapshot.MonthlyTotalUsd.Should().Be(0m);
        snapshot.SessionTotalsUsd.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordSpendAsync_accumulates_session_and_global_totals()
    {
        using var metrics = new LeanKernelMetrics();
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, TimeProvider.System);

        await tracker.RecordSpendAsync("session-1", "turn-1", 1.50m);
        await tracker.RecordSpendAsync("session-1", "turn-2", 2.50m);
        await tracker.RecordSpendAsync("session-2", "turn-1", 3.00m);

        var snapshot = tracker.GetSnapshot();
        snapshot.DailyTotalUsd.Should().Be(7.00m);
        snapshot.MonthlyTotalUsd.Should().Be(7.00m);
        snapshot.SessionTotalsUsd["session-1"].Should().Be(4.00m);
        snapshot.SessionTotalsUsd["session-2"].Should().Be(3.00m);
    }

    [Fact]
    public async Task RecordSpendAsync_normalizes_negative_amounts_to_zero()
    {
        using var metrics = new LeanKernelMetrics();
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, TimeProvider.System);

        await tracker.RecordSpendAsync("session-1", "turn-1", -5.00m);

        var snapshot = tracker.GetSnapshot();
        snapshot.DailyTotalUsd.Should().Be(0m);
    }

    [Fact]
    public async Task RecordSpendAsync_throws_on_empty_session_id()
    {
        using var metrics = new LeanKernelMetrics();
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => tracker.RecordSpendAsync("", "turn-1", 1.0m));
        await Assert.ThrowsAsync<ArgumentException>(() => tracker.RecordSpendAsync("session-1", "", 1.0m));
    }

    [Fact]
    public async Task GetSnapshot_respects_AsOf_parameter()
    {
        using var metrics = new LeanKernelMetrics();
        var timeProvider = new AdjustableTimeProvider(DateTimeOffset.Parse("2025-06-15T10:00:00Z"));
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, timeProvider);

        await tracker.RecordSpendAsync("session-1", "turn-1", 10.00m);

        var snapshot = tracker.GetSnapshot(DateTimeOffset.Parse("2025-06-15T10:00:00Z"));
        snapshot.DailyTotalUsd.Should().Be(10.00m);
    }

    [Fact]
    public async Task RecordSpendAsync_with_diagnosticsSink_persists_entries()
    {
        var sink = new RecordingDiagnosticsSink();
        using var metrics = new LeanKernelMetrics();
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, TimeProvider.System, sink);

        await tracker.RecordSpendAsync("session-1", "turn-1", 5.00m);

        sink.Entries.Should().ContainSingle();
        sink.Entries[0].SessionId.Should().Be("session-1");
        sink.Entries[0].Category.Should().Be("SpendTracking");
    }

    [Fact]
    public async Task RecordSpendAsync_swallows_diagnostics_sink_exception()
    {
        var throwingSink = new ThrowingDiagnosticsSink(new InvalidOperationException("DB down"));
        using var metrics = new LeanKernelMetrics();
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, TimeProvider.System, throwingSink);

        var snapshot = await tracker.RecordSpendAsync("session-1", "turn-1", 5.00m);

        snapshot.DailyTotalUsd.Should().Be(5.00m);
    }

    [Fact]
    public async Task Daily_boundary_rolls_over_daily_total()
    {
        using var metrics = new LeanKernelMetrics();
        var timeProvider = new AdjustableTimeProvider(DateTimeOffset.Parse("2025-06-15T23:59:00Z"));
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, timeProvider);

        await tracker.RecordSpendAsync("session-1", "turn-1", 10.00m);
        tracker.GetSnapshot().DailyTotalUsd.Should().Be(10.00m);

        timeProvider.Advance(TimeSpan.FromMinutes(2));

        var snapshot = tracker.GetSnapshot();
        snapshot.DailyTotalUsd.Should().Be(0m);
        snapshot.MonthlyTotalUsd.Should().Be(10.00m);
    }

    [Fact]
    public async Task Monthly_boundary_rolls_over_monthly_total()
    {
        using var metrics = new LeanKernelMetrics();
        var timeProvider = new AdjustableTimeProvider(DateTimeOffset.Parse("2025-06-30T23:59:00Z"));
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, timeProvider);

        await tracker.RecordSpendAsync("session-1", "turn-1", 100.00m);
        tracker.GetSnapshot().MonthlyTotalUsd.Should().Be(100.00m);

        timeProvider.Advance(TimeSpan.FromMinutes(2));

        var snapshot = tracker.GetSnapshot();
        snapshot.DailyTotalUsd.Should().Be(0m);
        snapshot.MonthlyTotalUsd.Should().Be(0m);
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private sealed class ThrowingDiagnosticsSink(Exception exception) : IDiagnosticsSink
    {
        public Task RecordAsync(DiagnosticEntry entry, CancellationToken ct = default)
            => Task.FromException(exception);

        public Task<IReadOnlyList<DiagnosticEntry>> GetEntriesAsync(string sessionId, CancellationToken ct = default)
            => Task.FromException<IReadOnlyList<DiagnosticEntry>>(exception);
    }

    private sealed class RecordingDiagnosticsSink : IDiagnosticsSink
    {
        public List<DiagnosticEntry> Entries { get; } = [];

        public Task RecordAsync(DiagnosticEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DiagnosticEntry>> GetEntriesAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DiagnosticEntry>>(Entries.Where(e => e.SessionId == sessionId).ToList());
    }
}
