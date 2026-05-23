namespace LeanKernel.Gateway.Services;

/// <summary>
/// Mock-backed admin dashboard service. Real admin endpoints can replace the internal data sources later
/// without changing the Blazor page structure or view models.
/// </summary>
public sealed class AdminService
{
    private readonly object _sync = new();
    private bool _initialized;
    private List<AdminProviderHealth> _providerHealth = [];
    private List<AdminToolGovernanceItem> _tools = [];

    public async Task<AdminDashboardSnapshot> GetDashboardAsync(CancellationToken ct = default)
    {
        await Task.Delay(450, ct).ConfigureAwait(false);

        lock (_sync)
        {
            EnsureInitialized();
            return BuildSnapshot(DateTimeOffset.UtcNow);
        }
    }

    public async Task<AdminDashboardSnapshot> RefreshProviderHealthAsync(CancellationToken ct = default)
    {
        await Task.Delay(300, ct).ConfigureAwait(false);

        lock (_sync)
        {
            EnsureInitialized();
            _providerHealth = BuildProviderHealth(DateTimeOffset.UtcNow);
            return BuildSnapshot(DateTimeOffset.UtcNow);
        }
    }

    public async Task<AdminDashboardSnapshot> SetToolEnabledAsync(string toolName, bool enabled, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        await Task.Delay(150, ct).ConfigureAwait(false);

        lock (_sync)
        {
            EnsureInitialized();

            var index = _tools.FindIndex(tool => string.Equals(tool.Name, toolName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _tools[index] = _tools[index] with { Enabled = enabled };
            }

            return BuildSnapshot(DateTimeOffset.UtcNow);
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        _providerHealth = BuildProviderHealth(now);
        _tools = BuildTools();
        _initialized = true;
    }

    private AdminDashboardSnapshot BuildSnapshot(DateTimeOffset generatedAt)
        => new(
            ProviderHealth: [.. _providerHealth],
            RoutingRules: BuildRoutingRules(),
            Tools: [.. _tools.OrderBy(tool => tool.Category, StringComparer.Ordinal).ThenBy(tool => tool.Name, StringComparer.Ordinal)],
            Spend: BuildSpendDashboard(generatedAt),
            ScheduledJobs: BuildScheduledJobs(generatedAt),
            GeneratedAt: generatedAt);

    private static List<AdminProviderHealth> BuildProviderHealth(DateTimeOffset now)
    {
        var anthropicStatus = Random.Shared.NextDouble() < 0.28 ? AdminProviderStatus.Degraded : AdminProviderStatus.Healthy;
        var azureStatus = Random.Shared.NextDouble() < 0.18 ? AdminProviderStatus.Unhealthy : AdminProviderStatus.Degraded;
        var ollamaStatus = Random.Shared.NextDouble() < 0.12 ? AdminProviderStatus.Unhealthy : AdminProviderStatus.Healthy;

        return
        [
            new("OpenAI", AdminProviderStatus.Healthy, 126 + Random.Shared.Next(-18, 22), now.AddSeconds(-Random.Shared.Next(18, 70)), "Primary frontier routing lane"),
            new("Anthropic", anthropicStatus, 164 + Random.Shared.Next(-28, 38), now.AddSeconds(-Random.Shared.Next(22, 84)), "Long-context reasoning lane"),
            new("Azure OpenAI", azureStatus, 242 + Random.Shared.Next(-35, 54), now.AddSeconds(-Random.Shared.Next(40, 132)), "Regional compliance and failover"),
            new("Ollama", ollamaStatus, 89 + Random.Shared.Next(-12, 20), now.AddSeconds(-Random.Shared.Next(16, 76)), "Local fallback and smoke-test path")
        ];
    }

    private static List<AdminToolGovernanceItem> BuildTools()
        =>
        [
            new("web_search", "Retrieval", true, "Global", "Query the open web for current events, release notes, and fast fact checks."),
            new("wiki_search", "Knowledge", true, "Workspace", "Search indexed internal knowledge and project notes with source-backed excerpts."),
            new("file_reader", "Workspace", true, "Workspace", "Read local repository files for code understanding and grounded responses."),
            new("shell_exec", "Execution", false, "Admin only", "Run reviewed shell commands for diagnostics and operational maintenance tasks."),
            new("model_router", "Runtime", true, "Global", "Select the best-fit provider lane based on task complexity, budget, and fallback rules."),
            new("job_trigger", "Scheduler", false, "Admin only", "Manually trigger selected scheduled jobs while runtime automation is under review."),
            new("diagnostics_export", "Observability", true, "Support", "Export recent traces, latency summaries, and provider incident context for support."),
            new("session_replay", "Operations", true, "Support", "Replay a stored session path for incident analysis and regression triage.")
        ];

    private static IReadOnlyList<AdminRoutingRule> BuildRoutingRules()
        =>
        [
            new("Low", "gpt-4.1-mini", "claude-haiku-4.5", 8_192, 0.38m),
            new("Medium", "claude-sonnet-4.6", "gpt-5-mini", 16_384, 1.85m),
            new("High", "gpt-5.4", "claude-sonnet-4.6", 32_768, 4.90m),
            new("Critical", "claude-opus-4.6", "gpt-5.4", 48_000, 11.75m)
        ];

    private static AdminSpendDashboard BuildSpendDashboard(DateTimeOffset now)
    {
        var dailySpend = Enumerable.Range(0, 7)
            .Select(offset => now.Date.AddDays(offset - 6))
            .Zip(new[] { 18.42m, 21.16m, 25.84m, 19.37m, 29.11m, 31.76m, 24.58m },
                (date, amount) => new AdminSpendPoint(date.ToString("MMM d"), amount, date == now.Date))
            .ToArray();

        return new AdminSpendDashboard(
            TodaySpend: dailySpend.Last().Amount,
            WeekSpend: dailySpend.Sum(point => point.Amount),
            MonthSpend: 412.19m,
            MonthlyBudgetLimit: 600m,
            DailySpend: dailySpend);
    }

    private static IReadOnlyList<AdminScheduledJob> BuildScheduledJobs(DateTimeOffset now)
        =>
        [
            new("Provider health poll", "*/5 * * * *", now.AddMinutes(-2), now.AddMinutes(3), AdminJobStatus.Running),
            new("Spend budget audit", "0 */6 * * *", now.AddHours(-4), now.AddHours(2), AdminJobStatus.Idle),
            new("Session compaction", "15 * * * *", now.AddMinutes(-43), now.AddMinutes(17), AdminJobStatus.Idle),
            new("Skill catalog refresh", "30 2 * * *", now.AddHours(-11), now.AddHours(13), AdminJobStatus.Failed)
        ];
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
    Failed,
}
