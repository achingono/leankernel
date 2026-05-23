using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Probes the health of one external provider dependency.
/// </summary>
public interface IProviderHealthProbe
{
    /// <summary>
    /// Gets the stable provider name.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Executes a provider-health probe.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The probe result.</returns>
    Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default);
}
