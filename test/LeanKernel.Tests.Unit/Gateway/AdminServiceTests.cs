using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Gateway.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Gateway;

public class AdminServiceTests
{
    private readonly Mock<IToolRegistry> _toolRegistryMock = new();
    private readonly Mock<HealthCheckService> _healthCheckServiceMock;
    private readonly IOptions<LeanKernelConfig> _options;
    private readonly LeanKernelConfig _config;

    public AdminServiceTests()
    {
        _config = new LeanKernelConfig
        {
            Routing = new RoutingConfig
            {
                Economy = new ModelTierConfig { Model = "gpt-4o-mini", MaxTokens = 4096, CostWeight = 0.5 },
                Standard = new ModelTierConfig { Model = "gpt-4o", MaxTokens = 8192, CostWeight = 1.0 },
                Premium = new ModelTierConfig { Model = "claude-sonnet", MaxTokens = 16384, CostWeight = 2.0 },
            },
            Scheduler = new SchedulerConfig
            {
                Enabled = true,
                Jobs =
                [
                    new ScheduledJobDefinition { Name = "cleanup", CronExpression = "0 * * * *", JobType = "maintenance", Enabled = true },
                    new ScheduledJobDefinition { Name = "digest", CronExpression = "0 9 * * 1", JobType = "digest", Enabled = false },
                ],
            },
        };
        _options = Options.Create(_config);
        _healthCheckServiceMock = new Mock<HealthCheckService>(MockBehavior.Loose);
    }

    private AdminService CreateService() => new(
        _toolRegistryMock.Object,
        _options,
        _healthCheckServiceMock.Object);

    [Fact]
    public async Task GetDashboardAsync_returns_snapshot_with_tools_config_and_health()
    {
        SetupToolRegistry("search", "codegen");
        SetupHealthyHealthCheck();

        var service = CreateService();
        var snapshot = await service.GetDashboardAsync();

        snapshot.Should().NotBeNull();
        snapshot.Tools.Should().HaveCount(2);
        snapshot.ProviderHealth.Should().NotBeEmpty();
        snapshot.RoutingRules.Should().HaveCount(3);
        snapshot.ScheduledJobs.Should().NotBeEmpty();
        snapshot.Spend.Should().NotBeNull();
        snapshot.GeneratedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetDashboardAsync_routing_rules_reflect_configuration()
    {
        SetupToolRegistry();
        SetupHealthyHealthCheck();

        var service = CreateService();
        var snapshot = await service.GetDashboardAsync();

        snapshot.RoutingRules[0].Tier.Should().Be("Economy");
        snapshot.RoutingRules[0].Model.Should().Be("gpt-4o-mini");
        snapshot.RoutingRules[0].MaxTokens.Should().Be(4096);
        snapshot.RoutingRules[0].CostPer1K.Should().Be(0.5m);

        snapshot.RoutingRules[1].Tier.Should().Be("Standard");
        snapshot.RoutingRules[1].Model.Should().Be("gpt-4o");
        snapshot.RoutingRules[1].FallbackModel.Should().Be("gpt-4o-mini");

        snapshot.RoutingRules[2].Tier.Should().Be("Premium");
        snapshot.RoutingRules[2].Model.Should().Be("claude-sonnet");
        snapshot.RoutingRules[2].FallbackModel.Should().Be("gpt-4o");
    }

    [Fact]
    public async Task GetDashboardAsync_tools_sorted_by_category_then_name()
    {
        SetupToolRegistryWithCategories(
            ("beta_tool", "Zeta"),
            ("alpha_tool", "Zeta"),
            ("gamma_tool", "Alpha"));
        SetupHealthyHealthCheck();

        var service = CreateService();
        var snapshot = await service.GetDashboardAsync();

        snapshot.Tools.Select(t => t.Name).Should().ContainInOrder("gamma_tool", "alpha_tool", "beta_tool");
    }

    [Fact]
    public async Task GetDashboardAsync_scheduler_disabled_returns_placeholder()
    {
        _config.Scheduler.Enabled = false;
        SetupToolRegistry();
        SetupHealthyHealthCheck();

        var service = CreateService();
        var snapshot = await service.GetDashboardAsync();

        snapshot.ScheduledJobs.Should().HaveCount(1);
        snapshot.ScheduledJobs[0].Name.Should().Be("Scheduler disabled");
        snapshot.ScheduledJobs[0].Status.Should().Be(AdminJobStatus.Idle);
    }

    [Fact]
    public async Task GetDashboardAsync_maps_enabled_and_disabled_job_statuses()
    {
        SetupToolRegistry();
        SetupHealthyHealthCheck();

        var service = CreateService();
        var snapshot = await service.GetDashboardAsync();

        var cleanup = snapshot.ScheduledJobs.First(j => j.Name == "cleanup");
        cleanup.Status.Should().Be(AdminJobStatus.Idle);

        var digest = snapshot.ScheduledJobs.First(j => j.Name == "digest");
        digest.Status.Should().Be(AdminJobStatus.Disabled);
    }

    [Fact]
    public async Task GetDashboardAsync_spend_dashboard_has_seven_days_and_budget()
    {
        SetupToolRegistry();
        SetupHealthyHealthCheck();

        var service = CreateService();
        var snapshot = await service.GetDashboardAsync();

        snapshot.Spend.DailySpend.Should().HaveCount(7);
        snapshot.Spend.TodaySpend.Should().BeGreaterThan(0);
        snapshot.Spend.WeekSpend.Should().BeGreaterThan(0);
        snapshot.Spend.MonthSpend.Should().Be(412.19m);
        snapshot.Spend.MonthlyBudgetLimit.Should().Be(600m);
    }

    [Fact]
    public async Task GetDashboardAsync_provider_health_maps_all_statuses()
    {
        SetupToolRegistry();
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["provider1"] = new(HealthStatus.Healthy, "ok", TimeSpan.FromMilliseconds(50), null, null),
            ["provider2"] = new(HealthStatus.Degraded, "slow", TimeSpan.FromMilliseconds(200), null, null),
            ["provider3"] = new(HealthStatus.Unhealthy, "down", TimeSpan.FromMilliseconds(5000), null, null),
        };
        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(300));
        _healthCheckServiceMock
            .Setup(h => h.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var service = CreateService();
        var snapshot = await service.GetDashboardAsync();

        snapshot.ProviderHealth.Should().HaveCount(3);
        snapshot.ProviderHealth[0].Status.Should().Be(AdminProviderStatus.Healthy);
        snapshot.ProviderHealth[0].LatencyMs.Should().Be(50);
        snapshot.ProviderHealth[1].Status.Should().Be(AdminProviderStatus.Degraded);
        snapshot.ProviderHealth[2].Status.Should().Be(AdminProviderStatus.Unhealthy);
    }

    [Fact]
    public async Task GetDashboardAsync_provider_health_fallback_on_exception()
    {
        SetupToolRegistry();
        _healthCheckServiceMock
            .Setup(h => h.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no health checks registered"));

        var service = CreateService();
        var snapshot = await service.GetDashboardAsync();

        snapshot.ProviderHealth.Should().HaveCount(1);
        snapshot.ProviderHealth[0].Name.Should().Be("LeanKernel Gateway");
        snapshot.ProviderHealth[0].Status.Should().Be(AdminProviderStatus.Healthy);
    }

    [Fact]
    public async Task SetToolEnabledAsync_disables_tool_and_returns_snapshot()
    {
        SetupToolRegistry("my_tool");
        SetupHealthyHealthCheck();

        var service = CreateService();
        var snapshot = await service.SetToolEnabledAsync("my_tool", false);

        snapshot.Tools.Single(t => t.Name == "my_tool").Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetToolEnabledAsync_reenables_previously_disabled_tool()
    {
        SetupToolRegistry("my_tool");
        SetupHealthyHealthCheck();

        var service = CreateService();
        await service.SetToolEnabledAsync("my_tool", false);
        var snapshot = await service.SetToolEnabledAsync("my_tool", true);

        snapshot.Tools.Single(t => t.Name == "my_tool").Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task SetToolEnabledAsync_is_case_insensitive()
    {
        SetupToolRegistry("My_Tool");
        SetupHealthyHealthCheck();

        var service = CreateService();
        await service.SetToolEnabledAsync("MY_TOOL", false);
        var snapshot = await service.GetDashboardAsync();

        snapshot.Tools.Single(t => t.Name == "My_Tool").Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetToolEnabledAsync_throws_on_null_or_whitespace()
    {
        var service = CreateService();

        var act = () => service.SetToolEnabledAsync("  ", true);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RefreshProviderHealthAsync_delegates_to_GetDashboardAsync()
    {
        SetupToolRegistry("tool_a");
        SetupHealthyHealthCheck();

        var service = CreateService();
        var snapshot = await service.RefreshProviderHealthAsync();

        snapshot.Should().NotBeNull();
        snapshot.ProviderHealth.Should().NotBeEmpty();
        snapshot.Tools.Should().NotBeEmpty();
        snapshot.RoutingRules.Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_throws_on_null_tool_registry()
    {
        var act = () => new AdminService(null!, _options, _healthCheckServiceMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("toolRegistry");
    }

    [Fact]
    public void Constructor_throws_on_null_config()
    {
        var act = () => new AdminService(_toolRegistryMock.Object, null!, _healthCheckServiceMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    private void SetupToolRegistry(params string[] toolNames)
    {
        var tools = toolNames.Select(name => new ToolDefinition
        {
            Name = name,
            Description = $"Description for {name}",
            Category = "General",
        }).ToList();

        _toolRegistryMock
            .Setup(r => r.GetVisibleTools(It.IsAny<ToolVisibilityContext>()))
            .Returns(tools);
    }

    private void SetupToolRegistryWithCategories(params (string Name, string Category)[] tools)
    {
        var definitions = tools.Select(t => new ToolDefinition
        {
            Name = t.Name,
            Description = $"Description for {t.Name}",
            Category = t.Category,
        }).ToList();

        _toolRegistryMock
            .Setup(r => r.GetVisibleTools(It.IsAny<ToolVisibilityContext>()))
            .Returns(definitions);
    }

    private void SetupHealthyHealthCheck()
    {
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["providers"] = new(HealthStatus.Healthy, "All OK", TimeSpan.FromMilliseconds(42), null, null),
        };
        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(42));
        _healthCheckServiceMock
            .Setup(h => h.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);
    }
}
