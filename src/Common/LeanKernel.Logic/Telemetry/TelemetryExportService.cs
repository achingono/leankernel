using LeanKernel.Data;
using LeanKernel.Logic.Telemetry.Models;
using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Logic.Telemetry;

/// <summary>
/// Exports deterministic, permit-scoped telemetry records without conversation content.
/// </summary>
public sealed class TelemetryExportService(
    IDbContextFactory<EntityContext> dbContextFactory,
    IPermit permit) : ITelemetryExportService
{
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
            throw new ArgumentException("Invalid date range: From must be <= To.", nameof(range));
    }

    private async Task<IReadOnlyList<TelemetryExportRecord>> ExportCoreAsync(
        DateRange range,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await dbContext.TurnTelemetry
            .AsNoTracking()
            .Where(row => row.Turn.Session.TenantId == permit.TenantId)
            .Where(row => row.Turn.Session.UserId == permit.UserId)
            .Where(row => row.Turn.Session.ChannelId == permit.ChannelId)
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
            return servedModel;

        return string.IsNullOrWhiteSpace(fallbackModel) ? "unknown" : fallbackModel;
    }
}
