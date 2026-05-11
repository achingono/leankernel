using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Performs deterministic, verified maintenance of engagement files.
/// </summary>
public interface IEngagementFileMaintenanceService
{
    /// <summary>
    /// Updates engagement files using only verified source content and deterministic cleanup rules.
    /// </summary>
    /// <param name="request">The maintenance request.</param>
    /// <param name="ct">A token used to cancel maintenance.</param>
    /// <returns>The verified maintenance result.</returns>
    Task<EngagementFileMaintenanceResult> MaintainAsync(
        EngagementFileMaintenanceRequest request,
        CancellationToken ct);
}
