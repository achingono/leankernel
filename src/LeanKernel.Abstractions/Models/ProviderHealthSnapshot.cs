namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Captures a point-in-time view of provider-health state.
/// </summary>
public sealed record ProviderHealthSnapshot
{
    /// <summary>
    /// Gets the provider-health map.
    /// </summary>
    public required IReadOnlyDictionary<string, ProviderHealthStatus> Providers { get; init; }

    /// <summary>
    /// Gets a value indicating whether all tracked providers are healthy.
    /// </summary>
    public bool AllHealthy => Providers.Count == 0 || Providers.Values.All(status => status.IsHealthy);

    /// <summary>
    /// Gets a provider-health status by name.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    /// <returns>The provider status when available; otherwise <see langword="null" />.</returns>
    public ProviderHealthStatus? GetProviderStatus(string providerName)
        => Providers.TryGetValue(providerName, out var status) ? status : null;
}
