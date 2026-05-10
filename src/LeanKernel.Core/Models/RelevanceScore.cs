using LeanKernel.Core.Enums;

namespace LeanKernel.Core.Models;

/// <summary>
/// Indicates the origin of a relevance result for source-aware scoring.
/// </summary>
public enum RelevanceSourceType
{
    /// <summary>Result from the wiki (multi-factor scoring).</summary>
    Wiki,
    /// <summary>Result from vector/knowledge search (semantic-only scoring).</summary>
    Vector
}

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

    /// <summary>Source type — determines which scoring formula to apply.</summary>
    public RelevanceSourceType SourceType { get; init; } = RelevanceSourceType.Wiki;

    /// <summary>Weighted composite scoring formula for wiki results.</summary>
    public static double ComputeScore(
        double semanticSimilarity,
        double recencyDecay,
        double dimensionMatch,
        double interactionFrequency)
        => (semanticSimilarity * 0.40)
         + (recencyDecay * 0.20)
         + (dimensionMatch * 0.25)
         + (interactionFrequency * 0.15);

    /// <summary>
    /// Computes a weighted composite score for wiki results using caller-supplied weights.
    /// </summary>
    /// <param name="semanticSimilarity">The semantic similarity component.</param>
    /// <param name="recencyDecay">The recency decay component.</param>
    /// <param name="dimensionMatch">The 5W1H dimension match component.</param>
    /// <param name="interactionFrequency">The interaction frequency component.</param>
    /// <param name="semanticSimilarityWeight">The semantic similarity weight.</param>
    /// <param name="recencyDecayWeight">The recency decay weight.</param>
    /// <param name="dimensionMatchWeight">The dimension match weight.</param>
    /// <param name="interactionFrequencyWeight">The interaction frequency weight.</param>
    /// <returns>The weighted composite score.</returns>
    public static double ComputeScore(
        double semanticSimilarity,
        double recencyDecay,
        double dimensionMatch,
        double interactionFrequency,
        double semanticSimilarityWeight,
        double recencyDecayWeight,
        double dimensionMatchWeight,
        double interactionFrequencyWeight)
        => (semanticSimilarity * semanticSimilarityWeight)
         + (recencyDecay * recencyDecayWeight)
         + (dimensionMatch * dimensionMatchWeight)
         + (interactionFrequency * interactionFrequencyWeight);

    /// <summary>
    /// Compute score based on source type. Vector results use semantic similarity directly;
    /// wiki results use the multi-factor formula.
    /// </summary>
    public double ComputeSourceAwareScore()
        => SourceType == RelevanceSourceType.Vector
            ? SemanticSimilarity
            : ComputeScore(SemanticSimilarity, RecencyDecay, DimensionMatch, InteractionFrequency);
}
