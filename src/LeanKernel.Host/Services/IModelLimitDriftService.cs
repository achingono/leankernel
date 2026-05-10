using LeanKernel.Host.Models.Routing;

namespace LeanKernel.Host.Services;

/// <summary>
/// Defines the contract for imodel limit drift service.
/// </summary>
public interface IModelLimitDriftService
{
    /// <summary>Run the drift script in dry-run mode and return the field-level diff.</summary>
    Task<DriftReport> PreviewDriftAsync(CancellationToken ct = default);
}
