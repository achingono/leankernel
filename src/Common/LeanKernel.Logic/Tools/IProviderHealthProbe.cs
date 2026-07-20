namespace LeanKernel.Logic.Tools;

/// <summary>
/// Probes the health of an external provider dependency.
/// </summary>
public interface IProviderHealthProbe
{
    /// <summary>
    /// Gets the provider name used for health probe registration.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Proves the health of the external provider.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ProviderProbeResult"/> indicating health status.</returns>
    Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default);
}