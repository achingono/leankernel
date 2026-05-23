using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Context.History;

public sealed class HistoryCompactionStrategy
{
    private readonly ITokenEstimator _tokenEstimator;
    private readonly HistoryConfig _config;
    private readonly ILogger<HistoryCompactionStrategy> _logger;

    public HistoryCompactionStrategy(
        ITokenEstimator tokenEstimator,
        IOptions<HistoryConfig> config,
        ILogger<HistoryCompactionStrategy> logger)
    {
        _tokenEstimator = tokenEstimator ?? throw new ArgumentNullException(nameof(tokenEstimator));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public HistoryCompactionPlan CreatePlan(IReadOnlyList<ConversationTurn> turns, int budgetTokens)
    {
        ArgumentNullException.ThrowIfNull(turns);

        var assignedEntries = new List<ShapedHistoryEntry>(turns.Count);
        var totalTokensBefore = turns.Sum(turn => _tokenEstimator.EstimateTokens(turn.Content));

        if (turns.Count == 0)
        {
            return new HistoryCompactionPlan
            {
                Diagnostics = new HistoryShapingDiagnostics
                {
                    BudgetAvailable = budgetTokens,
                    TotalTokensBefore = 0,
                    TotalTokensAfter = 0,
                    TotalTurns = 0,
                }
            };
        }

        var recentTurns = Math.Max(0, _config.RecentTurnsVerbatim);
        var compactedTurns = Math.Max(0, _config.CompactedTurnsMax);
        var summarizedTurns = Math.Max(0, _config.SummarizedTurnsMax);

        if (_config.EnableCompaction && !_config.EnableSummarization)
        {
            compactedTurns += summarizedTurns;
            summarizedTurns = 0;
        }
        else if (!_config.EnableCompaction && _config.EnableSummarization)
        {
            summarizedTurns += compactedTurns;
            compactedTurns = 0;
        }
        else if (!_config.EnableCompaction && !_config.EnableSummarization)
        {
            compactedTurns = 0;
            summarizedTurns = 0;
        }

        var verbatimCount = Math.Min(recentTurns, turns.Count);
        var remainingBeforeVerbatim = turns.Count - verbatimCount;
        var compactedCount = Math.Min(compactedTurns, remainingBeforeVerbatim);
        var remainingBeforeCompacted = remainingBeforeVerbatim - compactedCount;
        var summarizedCount = Math.Min(summarizedTurns, remainingBeforeCompacted);

        var verbatimStart = turns.Count - verbatimCount;
        var compactedStart = verbatimStart - compactedCount;
        var summarizedStart = compactedStart - summarizedCount;

        for (var index = 0; index < turns.Count; index++)
        {
            var turn = turns[index];
            var tokenCount = _tokenEstimator.EstimateTokens(turn.Content);
            var tier = index >= verbatimStart
                ? HistoryTier.Verbatim
                : index >= compactedStart
                    ? HistoryTier.Compacted
                    : index >= summarizedStart
                        ? HistoryTier.Summarized
                        : HistoryTier.Dropped;

            assignedEntries.Add(new ShapedHistoryEntry
            {
                Content = turn.Content,
                Role = turn.Role,
                Tier = tier,
                OriginalTimestamp = turn.Timestamp,
                OriginalTurnId = turn.TurnId,
                TokenCount = tokenCount,
            });
        }

        _logger.LogDebug(
            "History compaction plan created for {TotalTurns} turns: {Verbatim} verbatim, {Compacted} compacted, {Summarized} summarized, {Dropped} dropped",
            turns.Count,
            verbatimCount,
            compactedCount,
            summarizedCount,
            turns.Count - verbatimCount - compactedCount - summarizedCount);

        return new HistoryCompactionPlan
        {
            AssignedEntries = assignedEntries,
            VerbatimTurns = turns.Skip(verbatimStart).ToList(),
            CompactedTurns = turns.Skip(compactedStart).Take(compactedCount).ToList(),
            SummarizedTurns = turns.Skip(summarizedStart).Take(summarizedCount).ToList(),
            DroppedTurns = turns.Take(Math.Max(0, summarizedStart)).ToList(),
            Diagnostics = new HistoryShapingDiagnostics
            {
                TotalTurns = turns.Count,
                VerbatimTurns = verbatimCount,
                CompactedTurns = compactedCount,
                SummarizedTurns = summarizedCount,
                DroppedTurns = turns.Count - verbatimCount - compactedCount - summarizedCount,
                TotalTokensBefore = totalTokensBefore,
                TotalTokensAfter = assignedEntries.Where(entry => entry.Tier != HistoryTier.Dropped).Sum(entry => entry.TokenCount),
                BudgetAvailable = budgetTokens,
            }
        };
    }
}

public sealed record HistoryCompactionPlan
{
    public IReadOnlyList<ShapedHistoryEntry> AssignedEntries { get; init; } = [];
    public IReadOnlyList<ConversationTurn> VerbatimTurns { get; init; } = [];
    public IReadOnlyList<ConversationTurn> CompactedTurns { get; init; } = [];
    public IReadOnlyList<ConversationTurn> SummarizedTurns { get; init; } = [];
    public IReadOnlyList<ConversationTurn> DroppedTurns { get; init; } = [];
    public HistoryShapingDiagnostics Diagnostics { get; init; } = new();
}
