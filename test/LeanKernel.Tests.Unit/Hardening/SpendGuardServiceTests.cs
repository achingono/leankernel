using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Diagnostics;
using LeanKernel.Diagnostics.SpendGuard;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Hardening;

public class SpendGuardServiceTests
{
    [Fact]
    public void Evaluate_returns_allow_when_spend_guard_is_disabled()
    {
        // Arrange
        var timeProvider = new AdjustableTimeProvider(DateTimeOffset.Parse("2025-05-20T10:00:00Z"));
        using var metrics = new LeanKernelMetrics();
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, timeProvider);
        var service = new SpendGuardService(
            Options.Create(new HardeningConfig
            {
                SpendGuard = new SpendGuardConfig
                {
                    Enabled = false,
                }
            }),
            tracker,
            NullLogger<SpendGuardService>.Instance);

        // Act
        var decision = service.Evaluate("session-1", ModelTier.Standard, 4000, 1000);

        // Assert
        decision.Action.Should().Be(SpendGuardAction.Allow);
        decision.Reason.Should().Be("Spend guard is disabled.");
    }

    [Fact]
    public async Task Evaluate_returns_warn_when_projected_usage_crosses_warning_threshold()
    {
        // Arrange
        var timeProvider = new AdjustableTimeProvider(DateTimeOffset.Parse("2025-05-20T10:00:00Z"));
        using var metrics = new LeanKernelMetrics();
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, timeProvider);
        await tracker.RecordSpendAsync("session-1", "turn-1", 0.80m);
        var service = new SpendGuardService(
            Options.Create(new HardeningConfig
            {
                SpendGuard = new SpendGuardConfig
                {
                    Enabled = true,
                    MaxDailySpendUsd = 1.00m,
                    MaxSessionSpendUsd = 1.00m,
                    MaxMonthlySpendUsd = 10.00m,
                    WarnAtPercent = "80"
                }
            }),
            tracker,
            NullLogger<SpendGuardService>.Instance);

        // Act
        var decision = service.Evaluate("session-1", ModelTier.Economy, 10_000, 1_000);

        // Assert
        decision.Action.Should().Be(SpendGuardAction.Warn);
        decision.Reason.Should().Contain("warning threshold");
    }

    [Fact]
    public void Evaluate_returns_block_when_session_limit_would_be_exceeded()
    {
        // Arrange
        var timeProvider = new AdjustableTimeProvider(DateTimeOffset.Parse("2025-05-20T10:00:00Z"));
        using var metrics = new LeanKernelMetrics();
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, timeProvider);
        var service = new SpendGuardService(
            Options.Create(new HardeningConfig
            {
                SpendGuard = new SpendGuardConfig
                {
                    Enabled = true,
                    MaxDailySpendUsd = 10.00m,
                    MaxSessionSpendUsd = 0.01m,
                    MaxMonthlySpendUsd = 100.00m,
                    WarnAtPercent = "80"
                }
            }),
            tracker,
            NullLogger<SpendGuardService>.Instance);

        // Act
        var decision = service.Evaluate("session-1", ModelTier.Standard, 5_000, 0);

        // Assert
        decision.Action.Should().Be(SpendGuardAction.Block);
        decision.Reason.Should().Contain("session spend limit");
    }

    [Fact]
    public async Task Evaluate_returns_block_when_daily_limit_would_be_exceeded()
    {
        var timeProvider = new AdjustableTimeProvider(DateTimeOffset.Parse("2025-05-20T10:00:00Z"));
        using var metrics = new LeanKernelMetrics();
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, timeProvider);
        await tracker.RecordSpendAsync("session-1", "turn-1", 9.40m);
        var service = new SpendGuardService(
            Options.Create(new HardeningConfig
            {
                SpendGuard = new SpendGuardConfig
                {
                    Enabled = true,
                    MaxDailySpendUsd = 9.50m,
                    MaxSessionSpendUsd = 100.00m,
                    MaxMonthlySpendUsd = 100.00m,
                    WarnAtPercent = "80"
                }
            }),
            tracker,
            NullLogger<SpendGuardService>.Instance);

        var decision = service.Evaluate("session-2", ModelTier.Standard, 50_000, 0);

        decision.Action.Should().Be(SpendGuardAction.Block);
        decision.Reason.Should().Contain("daily spend limit");
    }

    [Fact]
    public async Task Evaluate_returns_block_when_monthly_limit_would_be_exceeded()
    {
        var timeProvider = new AdjustableTimeProvider(DateTimeOffset.Parse("2025-05-20T10:00:00Z"));
        using var metrics = new LeanKernelMetrics();
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, timeProvider);
        await tracker.RecordSpendAsync("session-1", "turn-1", 95.00m);
        var service = new SpendGuardService(
            Options.Create(new HardeningConfig
            {
                SpendGuard = new SpendGuardConfig
                {
                    Enabled = true,
                    MaxDailySpendUsd = 100.00m,
                    MaxSessionSpendUsd = 100.00m,
                    MaxMonthlySpendUsd = 96.00m,
                    WarnAtPercent = "80"
                }
            }),
            tracker,
            NullLogger<SpendGuardService>.Instance);

        var decision = service.Evaluate("session-2", ModelTier.Standard, 500_000, 0);

        decision.Action.Should().Be(SpendGuardAction.Block);
        decision.Reason.Should().Contain("monthly spend limit");
    }

    [Fact]
    public void EstimateCostUsd_returns_cost_for_standard_tier()
    {
        using var metrics = new LeanKernelMetrics();
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, TimeProvider.System);
        var service = new SpendGuardService(
            Options.Create(new HardeningConfig
            {
                SpendGuard = new SpendGuardConfig { Enabled = false }
            }),
            tracker,
            NullLogger<SpendGuardService>.Instance);

        var cost = service.EstimateCostUsd(ModelTier.Standard, 1000, 500);

        cost.Should().Be(0.0075m);
    }

    [Fact]
    public void EstimateCostUsd_falls_back_to_standard_for_unknown_tier()
    {
        using var metrics = new LeanKernelMetrics();
        var tracker = new SpendTracker(metrics, NullLogger<SpendTracker>.Instance, TimeProvider.System);
        var service = new SpendGuardService(
            Options.Create(new HardeningConfig
            {
                SpendGuard = new SpendGuardConfig { Enabled = false }
            }),
            tracker,
            NullLogger<SpendGuardService>.Instance);

        var cost = service.EstimateCostUsd((ModelTier)999, 1000, 500);

        cost.Should().Be(0.0075m);
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }
}
