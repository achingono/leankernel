using LeanKernel.Logic.Telemetry.Models;

namespace LeanKernel.Logic.Telemetry;

/// <summary>
/// Provides permit-scoped telemetry aggregations for reporting and diagnostics.
/// </summary>
public interface ITelemetryAggregationService
{
    /// <summary>
    /// Gets cost breakdown grouped by model.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by model.</returns>
    Task<IReadOnlyList<CostBreakdown>> GetCostByModelAsync(DateRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cost breakdown grouped by provider.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by provider.</returns>
    Task<IReadOnlyList<CostBreakdown>> GetCostByProviderAsync(DateRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cost breakdown grouped by user.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by user.</returns>
    Task<IReadOnlyList<CostBreakdown>> GetCostByUserAsync(DateRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cost breakdown grouped by session.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by session.</returns>
    Task<IReadOnlyList<CostBreakdown>> GetCostBySessionAsync(DateRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cost breakdown grouped by day.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by day.</returns>
    Task<IReadOnlyList<CostBreakdown>> GetCostByDayAsync(DateRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cost breakdown grouped by tenant.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by tenant.</returns>
    Task<IReadOnlyList<CostBreakdown>> GetCostByTenantAsync(DateRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cost breakdown grouped by model and day.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by model and day.</returns>
    Task<IReadOnlyList<CostBreakdown>> GetCostByModelAndDayAsync(DateRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cost breakdown grouped by provider and day.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by provider and day.</returns>
    Task<IReadOnlyList<CostBreakdown>> GetCostByProviderAndDayAsync(DateRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cost breakdown grouped by user and model.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns grouped by user and model.</returns>
    Task<IReadOnlyList<CostBreakdown>> GetCostByUserAndModelAsync(DateRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a summary of costs for the given date range.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="CostSummary"/> for the range.</returns>
    Task<CostSummary> GetSummaryAsync(DateRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets model efficiency metrics for the given date range.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of model efficiency metrics.</returns>
    Task<IReadOnlyList<ModelEfficiency>> GetModelEfficiencyAsync(DateRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the top users by cost for the given date range.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="top">The number of top users to return (default 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns for top users.</returns>
    Task<IReadOnlyList<CostBreakdown>> GetTopUsersByCostAsync(DateRange range, int top = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the top models by cost for the given date range.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="top">The number of top models to return (default 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of cost breakdowns for top models.</returns>
    Task<IReadOnlyList<CostBreakdown>> GetTopModelsByCostAsync(DateRange range, int top = 10, CancellationToken cancellationToken = default);
}