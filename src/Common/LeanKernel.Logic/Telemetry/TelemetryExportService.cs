using LeanKernel.Entities;
using LeanKernel.Logic.Interfaces;
using LeanKernel.Logic.Telemetry.Models;

using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Logic.Telemetry;

/// <summary>
/// Exports deterministic, permit-scoped telemetry records without conversation content.
/// All queries use <see cref="IRepository{TEntity}"/> to enforce scope predicates.
/// </summary>
public sealed class TelemetryExportService(
    IRepository<TurnTelemetryEntity> telemetryRepo) : ITelemetryExportService
{
    /// <summary>
    /// Exports telemetry records for the given date range.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of telemetry export records.</returns>
    public async Task<IReadOnlyList<TelemetryExportRecord>> ExportAsync(
        DateRange range,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(range);
        return await ExportCoreAsync(range, cancellationToken);
    }

    private static void ValidateRange(DateRange range)
    {
        if (!range.IsValid)
        {
            throw new ArgumentException("Invalid date range: From must be <= To.", nameof(range));
        }
    }

    private async Task<IReadOnlyList<TelemetryExportRecord>> ExportCoreAsync(
        DateRange range,
        CancellationToken cancellationToken)
    {
        var rows = await telemetryRepo.GetAll()
            .AsNoTracking()
            .Select(row => new TelemetryExportRecord(
                row.CapturedAt,
                string.IsNullOrWhiteSpace(row.RequestedModel) ? "unknown" : row.RequestedModel,
                ResolveServedModel(row.ServedModel, row.ModelId),
                string.IsNullOrWhiteSpace(row.Provider) ? "unknown" : row.Provider,
                row.PromptTokens ?? 0,
                row.CompletionTokens ?? 0,
                row.ResponseCost,
                row.CostIsEstimated))
            .ToListAsync(cancellationToken);

        return rows
            .Where(row => row.Timestamp >= range.From && row.Timestamp <= range.To)
            .OrderBy(row => row.Timestamp)
            .ThenBy(row => row.RequestedModel, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveServedModel(string? servedModel, string? fallbackModel)
    {
        if (!string.IsNullOrWhiteSpace(servedModel))
        {
            return servedModel;
        }

        return string.IsNullOrWhiteSpace(fallbackModel) ? "unknown" : fallbackModel;
    }
}