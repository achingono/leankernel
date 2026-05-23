namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Captures the authoritative and shadow-model outputs for one shadow-routed turn.
/// </summary>
public sealed record ShadowRoutingResult
{
    /// <summary>
    /// Gets the model used for the authoritative primary response.
    /// </summary>
    public required string PrimaryModel { get; init; }

    /// <summary>
    /// Gets the configured shadow model.
    /// </summary>
    public required string ShadowModel { get; init; }

    /// <summary>
    /// Gets the authoritative primary response text returned to the caller.
    /// </summary>
    public required string PrimaryResponse { get; init; }

    /// <summary>
    /// Gets the shadow-model response text recorded for comparison.
    /// </summary>
    public required string ShadowResponse { get; init; }

    /// <summary>
    /// Gets the elapsed time for the primary invocation.
    /// </summary>
    public TimeSpan PrimaryLatency { get; init; }

    /// <summary>
    /// Gets the elapsed time for the shadow invocation.
    /// </summary>
    public TimeSpan ShadowLatency { get; init; }

    /// <summary>
    /// Gets the best-effort token count recorded for the primary invocation.
    /// </summary>
    public int PrimaryTokensUsed { get; init; }

    /// <summary>
    /// Gets the best-effort token count recorded for the shadow invocation.
    /// </summary>
    public int ShadowTokensUsed { get; init; }

    /// <summary>
    /// Gets the derived comparison metadata between the primary and shadow responses.
    /// </summary>
    public ShadowComparison? Comparison { get; init; }
}

/// <summary>
/// Represents deterministic comparison metrics between primary and shadow outputs.
/// </summary>
public sealed record ShadowComparison
{
    /// <summary>
    /// Gets the ratio of shadow response length to primary response length.
    /// </summary>
    public double LengthRatio { get; init; }

    /// <summary>
    /// Gets a value indicating whether both responses contain non-whitespace content.
    /// </summary>
    public bool BothNonEmpty { get; init; }

    /// <summary>
    /// Gets a value indicating whether the primary response appears to refuse the request.
    /// </summary>
    public bool PrimaryRefusal { get; init; }

    /// <summary>
    /// Gets a value indicating whether the shadow response appears to refuse the request.
    /// </summary>
    public bool ShadowRefusal { get; init; }

    /// <summary>
    /// Gets optional comparison notes highlighting notable differences.
    /// </summary>
    public string? Notes { get; init; }
}
