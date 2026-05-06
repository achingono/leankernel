using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn.OpenclaSkills;

/// <summary>
/// Doughray personal finance API — read and manage accounts, transactions, 
/// budgets, categories, goals, and assets.
/// </summary>
[ToolMetadata(
    Name = "doughray_skill",
    Description = "Doughray personal finance API: read accounts, transactions, budgets, categories, goals, and assets. Use for spending analysis, budget tracking, net worth dashboards, and financial goal management.",
    Category = ToolCategory.General)]
public sealed class DoughraySkillTool : ITool
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl = "http://host.docker.internal:3030";

    public string Name => "doughray_skill";
    public string Description => "Access personal finance data via Doughray API.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "operation": { 
              "type": "string", 
              "description": "Operation: health, dashboard_summary, dashboard_trends, dashboard_spending, accounts_list, accounts_get, transactions_list, transactions_get, categories_list, budgets_list, goals_list, assets_list, sync_trigger, sync_status",
              "enum": ["health", "dashboard_summary", "dashboard_trends", "dashboard_spending", "accounts_list", "accounts_get", "transactions_list", "transactions_get", "categories_list", "budgets_list", "goals_list", "assets_list", "sync_trigger", "sync_status"]
            },
            "account_id": { "type": "string", "description": "Account ID" },
            "period": { "type": "string", "description": "Period for trends: number of months or 'all'" },
            "start_date": { "type": "string", "description": "Start date for spending analysis (YYYY-MM-DD)" },
            "end_date": { "type": "string", "description": "End date for spending analysis (YYYY-MM-DD)" }
          },
          "required": ["operation"]
        }
        """;

    public DoughraySkillTool(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            var root = doc.RootElement;
            var operation = root.GetProperty("operation").GetString() ?? "";

            var result = operation switch
            {
                "health" => await Health(ct),
                "dashboard_summary" => await DashboardSummary(root, ct),
                "dashboard_trends" => await DashboardTrends(root, ct),
                "dashboard_spending" => await DashboardSpending(root, ct),
                "accounts_list" => await AccountsList(ct),
                "accounts_get" => await AccountsGet(root, ct),
                "transactions_list" => await TransactionsList(root, ct),
                "transactions_get" => await TransactionsGet(root, ct),
                "categories_list" => await CategoriesList(ct),
                "budgets_list" => await BudgetsList(ct),
                "goals_list" => await GoalsList(ct),
                "assets_list" => await AssetsList(ct),
                "sync_trigger" => await SyncTrigger(ct),
                "sync_status" => await SyncStatus(ct),
                _ => $"Unknown operation: {operation}"
            };

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = result,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = ex.Message,
                Duration = sw.Elapsed
            };
        }
    }

    private async Task<string> Health(CancellationToken ct)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/health", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> DashboardSummary(JsonElement root, CancellationToken ct)
    {
        var url = $"{_baseUrl}/api/dashboard/summary";
        if (root.TryGetProperty("account_id", out var accountIdElem))
            url += $"?accountId={accountIdElem.GetString()}";

        var response = await _httpClient.GetAsync(url, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> DashboardTrends(JsonElement root, CancellationToken ct)
    {
        var period = root.TryGetProperty("period", out var periodElem) ? periodElem.GetString() : "6";
        var url = $"{_baseUrl}/api/dashboard/trends?period={period}";

        if (root.TryGetProperty("account_id", out var accountIdElem))
            url += $"&accountId={accountIdElem.GetString()}";

        var response = await _httpClient.GetAsync(url, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> DashboardSpending(JsonElement root, CancellationToken ct)
    {
        var startDate = root.GetProperty("start_date").GetString();
        var endDate = root.GetProperty("end_date").GetString();
        var url = $"{_baseUrl}/api/dashboard/spending-by-category?startDate={startDate}&endDate={endDate}";

        var response = await _httpClient.GetAsync(url, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> AccountsList(CancellationToken ct)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/accounts", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> AccountsGet(JsonElement root, CancellationToken ct)
    {
        var accountId = root.GetProperty("account_id").GetString();
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/accounts/{accountId}", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> TransactionsList(JsonElement root, CancellationToken ct)
    {
        var url = $"{_baseUrl}/api/transactions";
        if (root.TryGetProperty("account_id", out var accountIdElem))
            url += $"?accountId={accountIdElem.GetString()}";

        var response = await _httpClient.GetAsync(url, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> TransactionsGet(JsonElement root, CancellationToken ct)
    {
        var transactionId = root.GetProperty("account_id").GetString(); // Reusing for transaction ID
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/transactions/{transactionId}", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> CategoriesList(CancellationToken ct)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/categories", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> BudgetsList(CancellationToken ct)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/budgets", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> GoalsList(CancellationToken ct)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/goals", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> AssetsList(CancellationToken ct)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/assets", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> SyncTrigger(CancellationToken ct)
    {
        var response = await _httpClient.PostAsync($"{_baseUrl}/api/sync/trigger", null, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> SyncStatus(CancellationToken ct)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/sync/status", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }
}
