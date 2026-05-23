using LeanKernel.Abstractions.Enums;

namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Captures the selected model tier and the reasoning behind a routing decision.
/// </summary>
public sealed record RoutingDecision
{
    /// <summary>
    /// Gets the selected model tier.
    /// </summary>
    public required ModelTier SelectedTier { get; init; }

    /// <summary>
    /// Gets the selected model name.
    /// </summary>
    public required string SelectedModel { get; init; }

    /// <summary>
    /// Gets the computed complexity score.
    /// </summary>
    public required double ComplexityScore { get; init; }

    /// <summary>
    /// Gets the human-readable routing reason.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Gets the factors that contributed to the routing decision.
    /// </summary>
    public IReadOnlyList<string> Factors { get; init; } = [];

    /// <summary>
    /// Gets the tier that the current decision escalated from, if any.
    /// </summary>
    public ModelTier? EscalatedFrom { get; init; }

    /// <summary>
    /// Gets the escalation attempt number for the current decision.
    /// </summary>
    public int EscalationAttempt { get; init; }
}
