namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Describes the outcome of a provider-health probe.
/// </summary>
public sealed record ProviderProbeResult
{
    /// <summary>
    /// Gets a value indicating whether the provider is healthy.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Gets a human-readable probe description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the probe error message when unhealthy.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a healthy probe result.
    /// </summary>
    /// <param name="description">The probe description.</param>
    /// <returns>A healthy probe result.</returns>
    public static ProviderProbeResult Healthy(string description)
        => new()
        {
            IsHealthy = true,
            Description = description,
        };

    /// <summary>
    /// Creates an unhealthy probe result.
    /// </summary>
    /// <param name="description">The probe description.</param>
    /// <param name="errorMessage">The optional error message.</param>
    /// <returns>An unhealthy probe result.</returns>
    public static ProviderProbeResult Unhealthy(string description, string? errorMessage = null)
        => new()
        {
            IsHealthy = false,
            Description = description,
            ErrorMessage = errorMessage,
        };
}
