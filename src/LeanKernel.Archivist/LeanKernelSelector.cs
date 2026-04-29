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
        double semWeight = 0.40,
        double recWeight = 0.20,
        double dimWeight = 0.25,
        double freqWeight = 0.15)
        => (semanticSimilarity * semWeight)
         + (recencyDecay * recWeight)
         + (dimensionMatch * dimWeight)
         + (interactionFrequency * freqWeight);
}
