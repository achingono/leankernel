using LeanKernel.Logic.Telemetry.Models;

namespace LeanKernel.Logic.Telemetry;

/// <summary>
/// Produces deterministic, PII-free telemetry exports for learning workflows.
/// </summary>
public interface ITelemetryExportService
{
    /// <summary>
    /// Exports telemetry records for the given date range.
    /// </summary>
    /// <param name="range">The date range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of telemetry export records.</returns>
    Task<IReadOnlyList<TelemetryExportRecord>> ExportAsync(DateRange range, CancellationToken cancellationToken = default);
}