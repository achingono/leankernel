using LeanKernel.Logic.Telemetry.Models;

namespace LeanKernel.Logic.Telemetry;

/// <summary>
/// Provides permit-scoped telemetry aggregations for reporting and diagnostics.
/// </summary>
public interface ITelemetryAggregationService
{
    Task<IReadOnlyList<CostBreakdown>> GetCostByModelAsync(DateRange range, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CostBreakdown>> GetCostByProviderAsync(DateRange range, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CostBreakdown>> GetCostByUserAsync(DateRange range, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CostBreakdown>> GetCostBySessionAsync(DateRange range, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CostBreakdown>> GetCostByDayAsync(DateRange range, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CostBreakdown>> GetCostByTenantAsync(DateRange range, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CostBreakdown>> GetCostByModelAndDayAsync(DateRange range, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CostBreakdown>> GetCostByProviderAndDayAsync(DateRange range, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CostBreakdown>> GetCostByUserAndModelAsync(DateRange range, CancellationToken cancellationToken = default);

    Task<CostSummary> GetSummaryAsync(DateRange range, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModelEfficiency>> GetModelEfficiencyAsync(DateRange range, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CostBreakdown>> GetTopUsersByCostAsync(DateRange range, int top = 10, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CostBreakdown>> GetTopModelsByCostAsync(DateRange range, int top = 10, CancellationToken cancellationToken = default);
}