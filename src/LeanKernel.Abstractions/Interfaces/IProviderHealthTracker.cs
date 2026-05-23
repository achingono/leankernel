using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Tracks provider-health state across probes and runtime failures.
/// </summary>
public interface IProviderHealthTracker
{
    /// <summary>
    /// Gets the current provider-health snapshot.
    /// </summary>
    /// <returns>The current health snapshot.</returns>
    ProviderHealthSnapshot GetSnapshot();

    /// <summary>
    /// Gets the current status for a provider.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    /// <returns>The provider-health status.</returns>
    ProviderHealthStatus GetStatus(string providerName);

    /// <summary>
    /// Records a probe or runtime health observation for a provider.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="result">The probe result.</param>
    void RecordProbeResult(string providerName, ProviderProbeResult result);

    /// <summary>
    /// Refreshes all registered provider probes.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the refresh is finished.</returns>
    Task RefreshAsync(CancellationToken ct = default);
}
