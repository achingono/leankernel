using LeanKernel.Abstractions.Enums;

namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents the current health state for a provider.
/// </summary>
public sealed record ProviderHealthStatus
{
    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public required string ProviderName { get; init; }

    /// <summary>
    /// Gets the provider-health state.
    /// </summary>
    public ProviderHealthState State { get; init; } = ProviderHealthState.Healthy;

    /// <summary>
    /// Gets the last probe description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the last error message.
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// Gets the consecutive failure count.
    /// </summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>
    /// Gets the consecutive success count.
    /// </summary>
    public int ConsecutiveSuccesses { get; init; }

    /// <summary>
    /// Gets the last check timestamp.
    /// </summary>
    public DateTimeOffset LastCheckedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets a value indicating whether the provider is healthy.
    /// </summary>
    public bool IsHealthy => State == ProviderHealthState.Healthy;
}
