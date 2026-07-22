using LeanKernel.Entities;
using LeanKernel.Logic.Interfaces;
using LeanKernel.Logic.Telemetry.Models;

using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Logic.Telemetry;

/// <summary>
/// Aggregates persisted assistant-turn telemetry for the current request partition.
/// All queries use <see cref="IRepository{TEntity}"/> to enforce scope predicates.
/// </summary>
public sealed class TelemetryAggregationService(
    IRepository<TurnTelemetryEntity> telemetryRepo) : ITelemetryAggregationService
{
    /// <summary>
    /// Gets cost breakdown grouped by model.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by model.</returns>
    public async Task<IReadOnlyList<CostBreakdown>> GetCostByModelAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(range, cancellationToken);
        return BuildBreakdown(rows, "model", row => row.Model);
    }

    /// <summary>
    /// Gets cost breakdown grouped by provider.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by provider.</returns>
    public async Task<IReadOnlyList<CostBreakdown>> GetCostByProviderAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(range, cancellationToken);
        return BuildBreakdown(rows, "provider", row => row.Provider);
    }

    /// <summary>
    /// Gets cost breakdown grouped by user.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by user.</returns>
    public async Task<IReadOnlyList<CostBreakdown>> GetCostByUserAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(range, cancellationToken);
        return BuildBreakdown(rows, "user", row => row.UserId.ToString("D"));
    }

    /// <summary>
    /// Gets cost breakdown grouped by session.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by session.</returns>
    public async Task<IReadOnlyList<CostBreakdown>> GetCostBySessionAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(range, cancellationToken);
        return BuildBreakdown(rows, "session", row => row.SessionId.ToString("D"));
    }

    /// <summary>
    /// Gets cost breakdown grouped by day.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by day.</returns>
    public async Task<IReadOnlyList<CostBreakdown>> GetCostByDayAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(range, cancellationToken);
        return BuildBreakdown(rows, "day", row => row.CapturedAt.UtcDateTime.ToString("yyyy-MM-dd"));
    }

    /// <summary>
    /// Gets cost breakdown grouped by tenant.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by tenant.</returns>
    public async Task<IReadOnlyList<CostBreakdown>> GetCostByTenantAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(range, cancellationToken);
        return BuildBreakdown(rows, "tenant", row => row.TenantId.ToString("D"));
    }

    /// <summary>
    /// Gets cost breakdown grouped by model and day.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by model and day.</returns>
    public async Task<IReadOnlyList<CostBreakdown>> GetCostByModelAndDayAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(range, cancellationToken);
        return BuildBreakdown(
            rows,
            "model-day",
            row => $"{row.Model}|{row.CapturedAt.UtcDateTime:yyyy-MM-dd}");
    }

    /// <summary>
    /// Gets cost breakdown grouped by provider and day.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by provider and day.</returns>
    public async Task<IReadOnlyList<CostBreakdown>> GetCostByProviderAndDayAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(range, cancellationToken);
        return BuildBreakdown(
            rows,
            "provider-day",
            row => $"{row.Provider}|{row.CapturedAt.UtcDateTime:yyyy-MM-dd}");
    }

    /// <summary>
    /// Gets cost breakdown grouped by user and model.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by user and model.</returns>
    public async Task<IReadOnlyList<CostBreakdown>> GetCostByUserAndModelAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(range, cancellationToken);
        return BuildBreakdown(
            rows,
            "user-model",
            row => $"{row.UserId:D}|{row.Model}");
    }

    /// <summary>
    /// Gets a summary of costs for the given date range.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="CostSummary"/> for the range.</returns>
    public async Task<CostSummary> GetSummaryAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(range, cancellationToken);
        var totalCost = rows.Sum(row => row.ResponseCost ?? 0m);
        var totalPromptTokens = rows.Sum(row => row.PromptTokens);
        var totalCompletionTokens = rows.Sum(row => row.CompletionTokens);
        var totalTurns = rows.Count;
        var totalTokens = totalPromptTokens + totalCompletionTokens;

        return new CostSummary(
            range,
            totalCost,
            totalPromptTokens,
            totalCompletionTokens,
            totalTurns,
            rows.Select(row => row.UserId).Distinct().Count(),
            rows.Select(row => row.SessionId).Distinct().Count(),
            rows.Select(row => row.Model).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            totalTurns > 0 ? totalCost / totalTurns : 0m,
            totalTurns > 0 ? totalTokens / (decimal)totalTurns : 0m,
            rows.Select(row => row.Currency)
                .FirstOrDefault(currency => !string.IsNullOrWhiteSpace(currency))
            ?? "USD");
    }

    /// <summary>
    /// Gets model efficiency metrics for the given date range.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of model efficiency metrics.</returns>
    public async Task<IReadOnlyList<ModelEfficiency>> GetModelEfficiencyAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(range, cancellationToken);

        return rows
            .GroupBy(row => new { row.Model, row.Provider })
            .Select(group =>
            {
                var turnCount = group.Count();
                var promptTokens = group.Sum(row => row.PromptTokens);
                var completionTokens = group.Sum(row => row.CompletionTokens);
                var totalTokens = promptTokens + completionTokens;
                var totalCost = group.Sum(row => row.ResponseCost ?? 0m);
                var costPer1k = totalTokens > 0
                    ? totalCost / totalTokens * 1000m
                    : 0m;
                var completionRatio = totalTokens > 0
                    ? completionTokens / (decimal)totalTokens
                    : 0m;

                return new ModelEfficiency(
                    group.Key.Model,
                    group.Key.Provider,
                    turnCount,
                    totalTokens,
                    totalCost,
                    costPer1k,
                    turnCount > 0 ? promptTokens / (decimal)turnCount : 0m,
                    turnCount > 0 ? completionTokens / (decimal)turnCount : 0m,
                    completionRatio);
            })
            .OrderByDescending(metric => metric.TotalCost)
            .ThenBy(metric => metric.Model, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets the top users by cost for the given date range.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="top">The number of top users to return (default 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns for top users.</returns>
    public async Task<IReadOnlyList<CostBreakdown>> GetTopUsersByCostAsync(DateRange range, int top = 10, CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(range, cancellationToken);
        return BuildBreakdown(rows, "user", row => row.UserId.ToString("D"))
            .Take(Math.Max(1, top))
            .ToList();
    }

    /// <summary>
    /// Gets the top models by cost for the given date range.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="top">The number of top models to return (default 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns for top models.</returns>
    public async Task<IReadOnlyList<CostBreakdown>> GetTopModelsByCostAsync(DateRange range, int top = 10, CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(range, cancellationToken);
        return BuildBreakdown(rows, "model", row => row.Model)
            .Take(Math.Max(1, top))
            .ToList();
    }

    private async Task<List<TelemetryRow>> LoadRowsAsync(DateRange range, CancellationToken cancellationToken)
    {
        ValidateRange(range);
        return await LoadRowsCoreAsync(range, cancellationToken);
    }

    private static void ValidateRange(DateRange range)
    {
        if (!range.IsValid)
        {
            throw new ArgumentException("Invalid date range: From must be <= To.", nameof(range));
        }
    }

    private async Task<List<TelemetryRow>> LoadRowsCoreAsync(DateRange range, CancellationToken cancellationToken)
    {
        var rows = await telemetryRepo.GetAll()
            .AsNoTracking()
            .Select(row => new TelemetryRow(
                row.TurnId,
                row.Turn.SessionId,
                row.Turn.Session.UserId,
                row.Turn.Session.TenantId,
                row.CapturedAt,
                row.RequestedModel,
                row.ServedModel ?? row.ModelId,
                row.Provider,
                row.PromptTokens ?? 0,
                row.CompletionTokens ?? 0,
                row.TotalTokens ?? ((row.PromptTokens ?? 0) + (row.CompletionTokens ?? 0)),
                row.ResponseCost,
                row.Currency,
                row.CostIsEstimated))
            .ToListAsync(cancellationToken);

        return rows
            .Where(row => row.CapturedAt >= range.From && row.CapturedAt <= range.To)
            .ToList();
    }

    private static IReadOnlyList<CostBreakdown> BuildBreakdown(
        IReadOnlyList<TelemetryRow> rows,
        string dimension,
        Func<TelemetryRow, string> keySelector)
    {
        return rows
            .GroupBy(row => NormalizeKey(keySelector(row)))
            .Select(group =>
            {
                var turnCount = group.Count();
                var promptTokens = group.Sum(row => row.PromptTokens);
                var completionTokens = group.Sum(row => row.CompletionTokens);
                var totalTokens = group.Sum(row => row.TotalTokens);
                var totalCost = group.Sum(row => row.ResponseCost ?? 0m);
                var estimatedTurnCount = group.Count(row => row.CostIsEstimated);
                var reportedTurnCount = group.Count(row => !row.CostIsEstimated && row.ResponseCost.HasValue);

                return new CostBreakdown(
                    dimension,
                    group.Key,
                    totalCost,
                    promptTokens,
                    completionTokens,
                    totalTokens,
                    turnCount,
                    turnCount > 0 ? totalCost / turnCount : 0m,
                    turnCount > 0 ? totalTokens / (decimal)turnCount : 0m,
                    estimatedTurnCount,
                    reportedTurnCount);
            })
            .OrderByDescending(item => item.TotalCost)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeKey(string? key)
    {
        return string.IsNullOrWhiteSpace(key) ? "unknown" : key.Trim();
    }

    private sealed record TelemetryRow(
        Guid TurnId,
        Guid SessionId,
        Guid UserId,
        Guid TenantId,
        DateTimeOffset CapturedAt,
        string? RequestedModelName,
        string? ServedModelName,
        string? ProviderName,
        int PromptTokens,
        int CompletionTokens,
        int TotalTokens,
        decimal? ResponseCost,
        string? Currency,
        bool CostIsEstimated)
    {
        public string Model => NormalizeKey(ServedModelName ?? RequestedModelName);

        public string Provider => NormalizeKey(ProviderName);
    }
}