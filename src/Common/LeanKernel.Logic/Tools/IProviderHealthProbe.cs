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
    public bool IsHealthy { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? Detail { get; init; }

    public static ProviderProbeResult Healthy(string message) => new()
    {
        IsHealthy = true,
        Message = message
    };

    public static ProviderProbeResult Unhealthy(string message, string? detail = null) => new()
    {
        IsHealthy = false,
        Message = message,
        Detail = detail
    };
}