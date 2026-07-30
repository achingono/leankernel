using FluentAssertions;

using LeanKernel.Logic.Diagnostics;
using LeanKernel.Tests.Unit.TestDoubles;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using Xunit;

namespace LeanKernel.Tests.Unit.Diagnostics;

public class HealthAggregatorTests
{
    [Fact]
    public async Task IsHealthyAsync_AllHealthy_ReturnsTrue()
    {
        var service = new StubHealthCheckService(new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["litellm"] = new(HealthStatus.Healthy, "ok", TimeSpan.Zero, null, null),
                ["gbrain"] = new(HealthStatus.Healthy, "ok", TimeSpan.Zero, null, null),
            },
            HealthStatus.Healthy,
            TimeSpan.Zero));

        var aggregator = new HealthAggregator(service);
        var result = await aggregator.IsHealthyAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsHealthyAsync_Unhealthy_ReturnsFalse()
    {
        var service = new StubHealthCheckService(new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["litellm"] = new(HealthStatus.Unhealthy, "down", TimeSpan.Zero, null, null),
            },
            HealthStatus.Unhealthy,
            TimeSpan.Zero));

        var aggregator = new HealthAggregator(service);
        var result = await aggregator.IsHealthyAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsLiteLlmHealthyAsync_WhenHealthy_ReturnsTrue()
    {
        var service = new StubHealthCheckService(new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["litellm"] = new(HealthStatus.Healthy, "ok", TimeSpan.Zero, null, null),
            },
            HealthStatus.Healthy,
            TimeSpan.Zero));

        var aggregator = new HealthAggregator(service);

        var result = await aggregator.IsLiteLlmHealthyAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsLiteLlmHealthyAsync_WhenUnhealthy_ReturnsFalse()
    {
        var service = new StubHealthCheckService(new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["litellm"] = new(HealthStatus.Unhealthy, "down", TimeSpan.Zero, null, null),
            },
            HealthStatus.Unhealthy,
            TimeSpan.Zero));

        var aggregator = new HealthAggregator(service);

        var result = await aggregator.IsLiteLlmHealthyAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsLiteLlmHealthyAsync_WhenEntryMissing_ReturnsFalse()
    {
        var service = new StubHealthCheckService(new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["gbrain"] = new(HealthStatus.Healthy, "ok", TimeSpan.Zero, null, null),
            },
            HealthStatus.Healthy,
            TimeSpan.Zero));

        var aggregator = new HealthAggregator(service);

        var result = await aggregator.IsLiteLlmHealthyAsync();

        result.Should().BeFalse();
    }
}