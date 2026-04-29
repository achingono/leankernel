using LeanKernel.Core.Enums;

namespace LeanKernel.Core.Models;

/// <summary>
/// A scored retrieval result from the Archivist's relevance pipeline.
/// </summary>
public sealed record RelevanceScore
{
    public required string EntryId { get; init; }
    public required string Content { get; init; }
    public int EstimatedTokens { get; init; }

    /// <summary>Composite score (0.0–1.0). Higher = more relevant to current query.</summary>
    public double Score { get; init; }

    public double SemanticSimilarity { get; init; }
    public double RecencyDecay { get; init; }
    public double DimensionMatch { get; init; }
    public double InteractionFrequency { get; init; }
    public ContextPriority Priority { get; init; } = ContextPriority.Medium;

    /// <summary>Weighted composite scoring formula.</summary>
    public static double ComputeScore(
        double semanticSimilarity,
        double recencyDecay,
        double dimensionMatch,
        double interactionFrequency)
        => (semanticSimilarity * 0.40)
         + (recencyDecay * 0.20)
         + (dimensionMatch * 0.25)
         + (interactionFrequency * 0.15);
}
