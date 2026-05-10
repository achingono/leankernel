using LeanKernel.Core.Enums;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist;

/// <summary>
/// Token-budget-aware fact ranker. Implements the competitive scoring
/// formula and greedy budget filling.
/// </summary>
public static class LeanKernelSelector
{
    /// <summary>
    /// Select the best-fitting LeanKernels within a token budget using greedy selection.
    /// </summary>
    public static List<RelevanceScore> Select(
        IEnumerable<RelevanceScore> candidates,
        int tokenBudget,
        double minThreshold = 0.65)
    {
        var sorted = candidates
            .Where(c => c.Score >= minThreshold)
            .OrderByDescending(c => c.Score)
            .ToList();

        var selected = new List<RelevanceScore>();
        var remaining = tokenBudget;

        foreach (var LeanKernel in sorted)
        {
            if (LeanKernel.EstimatedTokens <= remaining)
            {
                selected.Add(LeanKernel);
                remaining -= LeanKernel.EstimatedTokens;
            }
        }

        return selected;
    }

    /// <summary>
    /// Compute a composite relevance score from individual signals.
    /// </summary>
    public static double Score(
        double semanticSimilarity,
        double recencyDecay,
        double dimensionMatch,
        double interactionFrequency,
        RelevanceScoreWeights? weights = null)
    {
        weights ??= RelevanceScoreWeights.Default;

        return (semanticSimilarity * weights.Semantic)
             + (recencyDecay * weights.Recency)
             + (dimensionMatch * weights.Dimension)
             + (interactionFrequency * weights.Frequency);
    }
}

/// <summary>
/// Represents the relevance score weights.
/// </summary>
public sealed record RelevanceScoreWeights(
    double Semantic = 0.40,
    double Recency = 0.20,
    double Dimension = 0.25,
    double Frequency = 0.15)
{
    /// <summary>
    /// Gets or sets the default.
    /// </summary>
    public static RelevanceScoreWeights Default { get; } = new();
}
