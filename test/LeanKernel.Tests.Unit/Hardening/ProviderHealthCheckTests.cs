using FluentAssertions;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Diagnostics.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;

namespace LeanKernel.Tests.Unit.Hardening;

public class ProviderHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_returns_healthy_when_all_providers_healthy()
    {
        var tracker = new Mock<IProviderHealthTracker>();
        tracker.Setup(t => t.GetSnapshot()).Returns(new ProviderHealthSnapshot
        {
            Providers = new Dictionary<string, ProviderHealthStatus>
            {
                ["litellm"] = new()
                {
                    ProviderName = "litellm",
                    State = ProviderHealthState.Healthy,
                    Description = "ok",
                    LastCheckedAt = DateTimeOffset.UtcNow,
                }
            }
        });

        var check = new ProviderHealthCheck(tracker.Object);
        var registration = new HealthCheckRegistration("test", check, HealthStatus.Unhealthy, null);
        var context = new HealthCheckContext { Registration = registration };

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("All tracked providers are healthy.");
        result.Data.Should().ContainKey("litellm");
    }

    [Fact]
    public async Task CheckHealthAsync_returns_unhealthy_when_some_providers_unhealthy()
    {
        var tracker = new Mock<IProviderHealthTracker>();
        tracker.Setup(t => t.GetSnapshot()).Returns(new ProviderHealthSnapshot
        {
            Providers = new Dictionary<string, ProviderHealthStatus>
            {
                ["litellm"] = new()
                {
                    ProviderName = "litellm",
                    State = ProviderHealthState.Unhealthy,
                    Description = "down",
                    LastError = "timeout",
                    ConsecutiveFailures = 3,
                    LastCheckedAt = DateTimeOffset.UtcNow,
                },
                ["gbrain"] = new()
                {
                    ProviderName = "gbrain",
                    State = ProviderHealthState.Healthy,
                    Description = "ok",
                    ConsecutiveSuccesses = 5,
                    LastCheckedAt = DateTimeOffset.UtcNow,
                }
            }
        });

        var check = new ProviderHealthCheck(tracker.Object);
        var registration = new HealthCheckRegistration("test", check, HealthStatus.Unhealthy, null);
        var context = new HealthCheckContext { Registration = registration };

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Unhealthy providers: litellm.");
        result.Data.Should().ContainKey("litellm");
        result.Data.Should().ContainKey("gbrain");
    }

    [Fact]
    public async Task CheckHealthAsync_returns_healthy_when_no_providers()
    {
        var tracker = new Mock<IProviderHealthTracker>();
        tracker.Setup(t => t.GetSnapshot()).Returns(new ProviderHealthSnapshot
        {
            Providers = new Dictionary<string, ProviderHealthStatus>()
        });

        var check = new ProviderHealthCheck(tracker.Object);
        var registration = new HealthCheckRegistration("test", check, HealthStatus.Unhealthy, null);
        var context = new HealthCheckContext { Registration = registration };

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void Constructor_throws_on_null_tracker()
    {
        Assert.Throws<ArgumentNullException>(() => new ProviderHealthCheck(null!));
    }
}
