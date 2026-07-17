using LeanKernel.Logic.Telemetry.Models;

namespace LeanKernel.Logic.Telemetry;

/// <summary>
/// Produces deterministic, PII-free telemetry exports for learning workflows.
/// </summary>
public interface ITelemetryExportService
{
    Task<IReadOnlyList<TelemetryExportRecord>> ExportAsync(DateRange range, CancellationToken cancellationToken = default);
}
