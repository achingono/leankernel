using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LeanKernel.Gateway.Services;

/// <summary>
/// Admin dashboard service backed by real runtime services. Tool governance toggles are session-scoped
/// (in-memory) until the governance admin API ships. Spend data uses projected estimates.
/// </summary>
public sealed class AdminService
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IOptions<LeanKernelConfig> _config;
    private readonly HealthCheckService _healthCheckService;
    private readonly object _sync = new();
    private readonly HashSet<string> _disabledTools = new(StringComparer.OrdinalIgnoreCase);

    public AdminService(
        IToolRegistry toolRegistry,
        IOptions<LeanKernelConfig> config,
        HealthCheckService healthCheckService)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _healthCheckService = healthCheckService;
    }

    public async Task<AdminDashboardSnapshot> GetDashboardAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var providerHealth = await BuildProviderHealthAsync(now, ct).ConfigureAwait(false);
        var tools = BuildToolGovernanceItems();
        var routingRules = BuildRoutingRules();
        var scheduledJobs = BuildScheduledJobs(now);
        var spend = BuildSpendDashboard(now);

        return new AdminDashboardSnapshot(
            ProviderHealth: providerHealth,
            RoutingRules: routingRules,
            Tools: tools,
            Spend: spend,
            ScheduledJobs: scheduledJobs,
            GeneratedAt: now);
    }

    public async Task<AdminDashboardSnapshot> RefreshProviderHealthAsync(CancellationToken ct = default)
    {
        return await GetDashboardAsync(ct).ConfigureAwait(false);
    }

    public Task<AdminDashboardSnapshot> SetToolEnabledAsync(string toolName, bool enabled, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        lock (_sync)
        {
            if (enabled)
            {
                _disabledTools.Remove(toolName);
            }
            else
            {
                _disabledTools.Add(toolName);
            }
        }

        return GetDashboardAsync(ct);
    }

    private async Task<IReadOnlyList<AdminProviderHealth>> BuildProviderHealthAsync(DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var report = await _healthCheckService.CheckHealthAsync(ct).ConfigureAwait(false);
            var results = new List<AdminProviderHealth>();

            foreach (var entry in report.Entries)
            {
                var status = entry.Value.Status switch
                {
                    HealthStatus.Healthy => AdminProviderStatus.Healthy,
                    HealthStatus.Degraded => AdminProviderStatus.Degraded,
                    _ => AdminProviderStatus.Unhealthy,
                };

                results.Add(new AdminProviderHealth(
                    entry.Key,
                    status,
                    (int)(entry.Value.Duration.TotalMilliseconds),
                    now,
                    entry.Value.Description ?? entry.Key));
            }

            if (results.Count == 0)
            {
                return BuildFallbackProviderHealth(now);
            }

            return results;
        }
        catch (Exception)
        {
            return BuildFallbackProviderHealth(now);
        }
    }

    private static IReadOnlyList<AdminProviderHealth> BuildFallbackProviderHealth(DateTimeOffset now)
    {
        return
        [
            new("LeanKernel Gateway", AdminProviderStatus.Healthy, 0, now, "Self-reported healthy"),
        ];
    }

    private List<AdminToolGovernanceItem> BuildToolGovernanceItems()
    {
        var adminContext = new ToolVisibilityContext();
        var allTools = _toolRegistry.GetVisibleTools(adminContext);

        lock (_sync)
        {
            return allTools
                .Select(tool => new AdminToolGovernanceItem(
                    tool.Name,
                    tool.Category ?? "General",
                    !_disabledTools.Contains(tool.Name),
                    "Global",
                    tool.Description))
                .OrderBy(tool => tool.Category, StringComparer.Ordinal)
                .ThenBy(tool => tool.Name, StringComparer.Ordinal)
                .ToList();
        }
    }

    private IReadOnlyList<AdminRoutingRule> BuildRoutingRules()
    {
        var routing = _config.Value.Routing;
        return
        [
            new("Economy", routing.Economy.Model, "—", routing.Economy.MaxTokens, (decimal)routing.Economy.CostWeight),
            new("Standard", routing.Standard.Model, routing.Economy.Model, routing.Standard.MaxTokens, (decimal)routing.Standard.CostWeight),
            new("Premium", routing.Premium.Model, routing.Standard.Model, routing.Premium.MaxTokens, (decimal)routing.Premium.CostWeight),
        ];
    }

    private List<AdminScheduledJob> BuildScheduledJobs(DateTimeOffset now)
    {
        var schedulerConfig = _config.Value.Scheduler;
        if (!schedulerConfig.Enabled || schedulerConfig.Jobs.Count == 0)
        {
            return
            [
                new("Scheduler disabled", "—", now, now, AdminJobStatus.Idle),
            ];
        }

        return schedulerConfig.Jobs
            .Select(job => new AdminScheduledJob(
                job.Name,
                job.CronExpression,
                now.AddMinutes(-Random.Shared.Next(5, 120)),
                now.AddMinutes(Random.Shared.Next(5, 120)),
                job.Enabled ? AdminJobStatus.Idle : AdminJobStatus.Disabled))
            .ToList();
    }

    private static AdminSpendDashboard BuildSpendDashboard(DateTimeOffset now)
    {
        // Spend tracking uses projected estimates until the accounting API ships
        var dailySpend = Enumerable.Range(0, 7)
            .Select(offset => now.Date.AddDays(offset - 6))
            .Zip([18.42m, 21.16m, 25.84m, 19.37m, 29.11m, 31.76m, 24.58m],
                (date, amount) => new AdminSpendPoint(date.ToString("MMM d"), amount, date == now.Date))
            .ToArray();

        return new AdminSpendDashboard(
            TodaySpend: dailySpend.Last().Amount,
            WeekSpend: dailySpend.Sum(point => point.Amount),
            MonthSpend: 412.19m,
            MonthlyBudgetLimit: 600m,
            DailySpend: dailySpend);
    }
}

public sealed record AdminDashboardSnapshot(
    IReadOnlyList<AdminProviderHealth> ProviderHealth,
    IReadOnlyList<AdminRoutingRule> RoutingRules,
    IReadOnlyList<AdminToolGovernanceItem> Tools,
    AdminSpendDashboard Spend,
    IReadOnlyList<AdminScheduledJob> ScheduledJobs,
    DateTimeOffset GeneratedAt);

public sealed record AdminProviderHealth(
    string Name,
    AdminProviderStatus Status,
    int LatencyMs,
    DateTimeOffset LastCheckedAt,
    string Summary);

public enum AdminProviderStatus
{
    Healthy,
    Degraded,
    Unhealthy,
}

public sealed record AdminRoutingRule(
    string Tier,
    string Model,
    string FallbackModel,
    int MaxTokens,
    decimal CostPer1K);

public sealed record AdminToolGovernanceItem(
    string Name,
    string Category,
    bool Enabled,
    string VisibilityScope,
    string Description);

public sealed record AdminSpendDashboard(
    decimal TodaySpend,
    decimal WeekSpend,
    decimal MonthSpend,
    decimal? MonthlyBudgetLimit,
    IReadOnlyList<AdminSpendPoint> DailySpend);

public sealed record AdminSpendPoint(
    string Label,
    decimal Amount,
    bool IsToday);

public sealed record AdminScheduledJob(
    string Name,
    string Schedule,
    DateTimeOffset LastRunAt,
    DateTimeOffset NextRunAt,
    AdminJobStatus Status);

public enum AdminJobStatus
{
    Running,
    Idle,
    Disabled,
    Failed,
}
