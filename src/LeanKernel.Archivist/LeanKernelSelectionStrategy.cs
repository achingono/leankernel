using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist;

/// <summary>
/// Default relevance strategy that scores LeanKernels and greedily fills the available token budget.
/// </summary>
public sealed class LeanKernelSelectionStrategy : ILeanKernelSelectionStrategy
{
    private readonly LeanKernelConfig _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeanKernelSelectionStrategy" /> class.
    /// </summary>
    /// <param name="config">The LeanKernel configuration that contains context selection thresholds.</param>
    public LeanKernelSelectionStrategy(IOptions<LeanKernelConfig> config)
    {
        _config = config.Value;
    }

    /// <inheritdoc />
    public IReadOnlyList<RelevanceScore> Select(
        IReadOnlyList<RelevanceScore> candidates,
        int tokenBudget,
        IList<string> exclusionLog)
    {
        var cfg = _config.Context;
        var scored = candidates.Select(c => c with
        {
            Score = c.SourceType == RelevanceSourceType.Vector
                ? c.SemanticSimilarity
                : RelevanceScore.ComputeScore(
                    c.SemanticSimilarity,
                    c.RecencyDecay,
                    c.DimensionMatch,
                    c.InteractionFrequency,
                    cfg.SemanticSimilarityWeight,
                    cfg.RecencyDecayWeight,
                    cfg.DimensionMatchWeight,
                    cfg.InteractionFrequencyWeight)
        })
        .OrderByDescending(c => c.Score)
        .ToList();

        var selected = new List<RelevanceScore>();
        var remainingBudget = tokenBudget;

        foreach (var leanKernel in scored)
        {
            if (leanKernel.Score < cfg.MinRelevanceThreshold)
            {
                exclusionLog.Add($"EXCLUDED [{leanKernel.EntryId}]: score {leanKernel.Score:F2} below threshold {cfg.MinRelevanceThreshold}");
                continue;
            }

            if (leanKernel.EstimatedTokens > remainingBudget)
            {
                exclusionLog.Add($"EXCLUDED [{leanKernel.EntryId}]: {leanKernel.EstimatedTokens} tokens exceeds remaining budget {remainingBudget}");
                continue;
            }

            selected.Add(leanKernel);
            remainingBudget -= leanKernel.EstimatedTokens;
        }

        return selected;
    }
}
