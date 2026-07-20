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
    Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default);
}

/// <summary>
/// Canonical provider name constants.
/// </summary>
public static class ProviderNames
{
    /// <summary>
    /// The webwright browser automation sidecar provider.
    /// </summary>
    public const string Webwright = "webwright";
}

/// <summary>
/// Result of a provider health probe.
/// </summary>
public sealed class ProviderProbeResult
{
    /// <summary>
    /// Gets a value indicating whether the provider is healthy.
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// Gets a message describing the health status.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets optional detail about the health probe result.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Creates a healthy probe result.
    /// </summary>
    /// <param name="message">A message describing the healthy status.</param>
    /// <returns>A healthy <see cref="ProviderProbeResult"/>.</returns>
    public static ProviderProbeResult Healthy(string message) => new()
    {
        IsHealthy = true,
        Message = message
    };

    /// <summary>
    /// Creates an unhealthy probe result.
    /// </summary>
    /// <param name="message">A message describing the unhealthy status.</param>
    /// <param name="detail">Optional detail about the failure.</param>
    /// <returns>An unhealthy <see cref="ProviderProbeResult"/>.</returns>
    public static ProviderProbeResult Unhealthy(string message, string? detail = null) => new()
    {
        IsHealthy = false,
        Message = message,
        Detail = detail
    };
}