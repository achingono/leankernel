using LeanKernel.Core.Enums;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist;

/// <summary>
/// Combines vector similarity, recency, dimension match, and interaction frequency
/// into a unified relevance score. Used by the ContextGatekeeper during ranking.
/// </summary>
public static class RelevanceScorer
{
    /// <summary>
    /// Enrich a candidate LeanKernel with individual signal scores, then compute composite.
    /// </summary>
    public static RelevanceScore Enrich(
        RelevanceScore candidate,
        HashSet<WikiDimension> activeDimensions,
        double semanticSimilarity = 0.0)
    {
        return candidate with
        {
            SemanticSimilarity = semanticSimilarity,
            Score = RelevanceScore.ComputeScore(
                semanticSimilarity,
                candidate.RecencyDecay,
                candidate.DimensionMatch,
                candidate.InteractionFrequency)
        };
    }

    /// <summary>
    /// Compute recency decay: 1.0 for today, linearly decaying to 0.0 at <paramref name="decayDays"/>.
    /// </summary>
    public static double RecencyDecay(DateTimeOffset lastAccessed, double decayDays = 90.0)
    {
        var daysSince = (DateTimeOffset.UtcNow - lastAccessed).TotalDays;
        return Math.Clamp(1.0 - (daysSince / decayDays), 0.0, 1.0);
    }

    /// <summary>
    /// Compute dimension match: 1.0 if the entry's dimension is active, 0.2 otherwise
    /// (cross-dimensional facts still get a small boost).
    /// </summary>
    public static double DimensionMatch(WikiDimension entryDimension, HashSet<WikiDimension> activeDimensions) =>
        activeDimensions.Contains(entryDimension) ? 1.0 : 0.2;

    /// <summary>
    /// Normalize interaction frequency: logarithmic scaling capped at 1.0.
    /// </summary>
    public static double InteractionFrequency(int accessCount, int maxAccessCount = 100) =>
        accessCount <= 0 ? 0.0 : Math.Min(Math.Log(accessCount + 1) / Math.Log(maxAccessCount + 1), 1.0);
}
