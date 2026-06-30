using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Diagnostics;
using LeanKernel.Diagnostics.Health;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Hardening;

public class ProviderHealthTrackerTests
{
    [Fact]
    public void RecordProbeResult_marks_provider_unhealthy_after_threshold_failures()
    {
        // Arrange
        using var metrics = new LeanKernelMetrics();
        var tracker = CreateTracker(metrics, unhealthyThreshold: 3, healthyThreshold: 2);

        // Act
        tracker.RecordProbeResult(ProviderNames.LiteLlm, ProviderProbeResult.Unhealthy("failure-1"));
        tracker.RecordProbeResult(ProviderNames.LiteLlm, ProviderProbeResult.Unhealthy("failure-2"));
        var beforeThreshold = tracker.GetStatus(ProviderNames.LiteLlm);
        tracker.RecordProbeResult(ProviderNames.LiteLlm, ProviderProbeResult.Unhealthy("failure-3"));
        var afterThreshold = tracker.GetStatus(ProviderNames.LiteLlm);

        // Assert
        beforeThreshold.IsHealthy.Should().BeTrue();
        afterThreshold.IsHealthy.Should().BeFalse();
        afterThreshold.ConsecutiveFailures.Should().Be(3);
    }

    [Fact]
    public void RecordProbeResult_recovers_provider_after_threshold_successes()
    {
        // Arrange
        using var metrics = new LeanKernelMetrics();
        var tracker = CreateTracker(metrics, unhealthyThreshold: 2, healthyThreshold: 2);
        tracker.RecordProbeResult(ProviderNames.GBrain, ProviderProbeResult.Unhealthy("failure-1"));
        tracker.RecordProbeResult(ProviderNames.GBrain, ProviderProbeResult.Unhealthy("failure-2"));

        // Act
        tracker.RecordProbeResult(ProviderNames.GBrain, ProviderProbeResult.Healthy("success-1"));
        var beforeRecovery = tracker.GetStatus(ProviderNames.GBrain);
        tracker.RecordProbeResult(ProviderNames.GBrain, ProviderProbeResult.Healthy("success-2"));
        var afterRecovery = tracker.GetStatus(ProviderNames.GBrain);

        // Assert
        beforeRecovery.IsHealthy.Should().BeFalse();
        afterRecovery.IsHealthy.Should().BeTrue();
        afterRecovery.ConsecutiveSuccesses.Should().Be(2);
    }

    [Fact]
    public void GetSnapshot_returns_current_provider_statuses()
    {
        using var metrics = new LeanKernelMetrics();
        var tracker = CreateTracker(metrics, unhealthyThreshold: 2, healthyThreshold: 2);
        tracker.RecordProbeResult(ProviderNames.LiteLlm, ProviderProbeResult.Unhealthy("fail"));

        var snapshot = tracker.GetSnapshot();

        snapshot.Providers.Should().ContainKey(ProviderNames.LiteLlm);
        snapshot.Providers[ProviderNames.LiteLlm].ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public void GetStatus_creates_initial_status_for_unknown_provider()
    {
        using var metrics = new LeanKernelMetrics();
        var tracker = CreateTracker(metrics, unhealthyThreshold: 3, healthyThreshold: 2);

        var status = tracker.GetStatus("new-provider");

        status.State.Should().Be(ProviderHealthState.Healthy);
        status.Description.Should().Be("Provider has not yet been probed.");
        status.ConsecutiveFailures.Should().Be(0);
        status.ConsecutiveSuccesses.Should().Be(0);
    }

    [Fact]
    public void GetStatus_returns_existing_status_for_registered_provider()
    {
        using var metrics = new LeanKernelMetrics();
        var tracker = CreateTracker(metrics, unhealthyThreshold: 3, healthyThreshold: 2);
        tracker.RecordProbeResult(ProviderNames.LiteLlm, ProviderProbeResult.Unhealthy("db down"));

        var status = tracker.GetStatus(ProviderNames.LiteLlm);

        status.State.Should().Be(ProviderHealthState.Healthy);
        status.Description.Should().Be("db down");
        status.ConsecutiveFailures.Should().Be(1);
        status.ConsecutiveSuccesses.Should().Be(0);
    }

    [Fact]
    public void GetStatus_throws_on_null_or_whitespace_name()
    {
        using var metrics = new LeanKernelMetrics();
        var tracker = CreateTracker(metrics, unhealthyThreshold: 3, healthyThreshold: 2);

        Assert.Throws<ArgumentNullException>(() => tracker.GetStatus(null!));
        Assert.Throws<ArgumentException>(() => tracker.GetStatus(""));
    }

    [Fact]
    public void RecordProbeResult_throws_on_null_result()
    {
        using var metrics = new LeanKernelMetrics();
        var tracker = CreateTracker(metrics, unhealthyThreshold: 3, healthyThreshold: 2);

        Assert.Throws<ArgumentNullException>(() => tracker.RecordProbeResult(ProviderNames.LiteLlm, null!));
    }

    private static ProviderHealthTracker CreateTracker(LeanKernelMetrics metrics, int unhealthyThreshold, int healthyThreshold)
        => new(
            Array.Empty<IProviderHealthProbe>(),
            Options.Create(new HardeningConfig
            {
                HealthTracking = new HealthTrackingConfig
                {
                    UnhealthyThreshold = unhealthyThreshold,
                    HealthyThreshold = healthyThreshold,
                    CheckIntervalSeconds = 30,
                }
            }),
            metrics,
            NullLogger<ProviderHealthTracker>.Instance,
            TimeProvider.System);
}
