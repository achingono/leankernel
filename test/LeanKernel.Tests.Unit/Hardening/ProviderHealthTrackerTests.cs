using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
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
