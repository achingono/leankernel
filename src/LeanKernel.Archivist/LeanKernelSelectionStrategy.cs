using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
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
                : ApplyEntityPriorityBoost(
                    RelevanceScore.ComputeScore(
                    c.SemanticSimilarity,
                    c.RecencyDecay,
                    c.DimensionMatch,
                    c.InteractionFrequency,
                    cfg.SemanticSimilarityWeight,
                    cfg.RecencyDecayWeight,
                    cfg.DimensionMatchWeight,
                    cfg.InteractionFrequencyWeight),
                    c.Priority,
                    cfg.EntitySubjectBoost)
        })
        .OrderByDescending(c => c.Score)
        .ToList();

        var selected = new List<RelevanceScore>();
        var remainingBudget = tokenBudget;

        foreach (var leanKernel in scored)
        {
            if (leanKernel.Priority == ContextPriority.Exclude)
            {
                exclusionLog.Add($"EXCLUDED [{leanKernel.EntryId}]: marked as exclude priority");
                continue;
            }

            var threshold = ResolveThreshold(leanKernel, cfg);
            if (leanKernel.Score < threshold)
            {
                exclusionLog.Add($"EXCLUDED [{leanKernel.EntryId}]: score {leanKernel.Score:F2} below threshold {threshold:F2}");
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

    private static double ResolveThreshold(RelevanceScore leanKernel, ContextConfig cfg)
    {
        if (leanKernel.SourceType != RelevanceSourceType.Wiki)
        {
            return cfg.MinRelevanceThreshold;
        }

        return leanKernel.Priority switch
        {
            ContextPriority.Critical => 0.0,
            ContextPriority.High => 0.0,
            ContextPriority.Low => Math.Min(cfg.MinRelevanceThreshold, cfg.SupportingEntityThreshold),
            _ => cfg.MinRelevanceThreshold
        };
    }

    private static double ApplyEntityPriorityBoost(double score, ContextPriority priority, double entityBoost)
    {
        if (priority is ContextPriority.Critical or ContextPriority.High)
        {
            return Math.Clamp(score + entityBoost, 0.0, 1.0);
        }

        return score;
    }
}
