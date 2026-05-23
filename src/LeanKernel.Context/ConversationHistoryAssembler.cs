using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context.History;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Context;

public sealed class ConversationHistoryAssembler
{
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ContextConfig _config;
    private readonly HistoryConfig _historyConfig;
    private readonly HistoryShaper? _historyShaper;
    private readonly ILogger<ConversationHistoryAssembler> _logger;

    public ConversationHistoryAssembler(
        ITokenEstimator tokenEstimator,
        IOptions<ContextConfig> config,
        ILogger<ConversationHistoryAssembler> logger,
        IOptions<HistoryConfig>? historyConfig = null,
        HistoryShaper? historyShaper = null)
    {
        _tokenEstimator = tokenEstimator ?? throw new ArgumentNullException(nameof(tokenEstimator));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _historyConfig = historyConfig?.Value ?? new HistoryConfig
        {
            RecentTurnsVerbatim = _config.RecentTurnsVerbatim,
            CompactedTurnsMax = _config.CompactedTurnsMax,
            SummarizedTurnsMax = 0,
            EnableCompaction = false,
            EnableSummarization = false,
            PersistCompactionMarkers = false,
        };
        _historyShaper = historyShaper;
    }

    public IReadOnlyList<ConversationTurn> Assemble(
        IReadOnlyList<ConversationTurn> fullHistory,
        int budgetTokens)
        => AssembleSimple(fullHistory, budgetTokens);

    public async Task<HistoryShapingResult> AssembleAsync(
        string sessionId,
        IReadOnlyList<ConversationTurn> fullHistory,
        int budgetTokens,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(fullHistory);

        if (fullHistory.Count == 0 || budgetTokens <= 0)
        {
            return new HistoryShapingResult
            {
                Diagnostics = new HistoryShapingDiagnostics
                {
                    TotalTurns = fullHistory.Count,
                    DroppedTurns = fullHistory.Count,
                    TotalTokensBefore = fullHistory.Sum(turn => _tokenEstimator.EstimateTokens(turn.Content)),
                    BudgetAvailable = budgetTokens,
                }
            };
        }

        if ((_historyConfig.EnableCompaction || _historyConfig.EnableSummarization) && _historyShaper is not null)
        {
            return await _historyShaper.ShapeAsync(sessionId, fullHistory, budgetTokens, ct).ConfigureAwait(false);
        }

        var history = AssembleSimple(fullHistory, budgetTokens);
        var totalTokensBefore = fullHistory.Sum(turn => _tokenEstimator.EstimateTokens(turn.Content));
        var totalTokensAfter = history.Sum(turn => _tokenEstimator.EstimateTokens(turn.Content));

        return new HistoryShapingResult
        {
            History = history,
            Entries = history.Select(turn => new ShapedHistoryEntry
            {
                Content = turn.Content,
                Role = turn.Role,
                Tier = HistoryTier.Verbatim,
                OriginalTimestamp = turn.Timestamp,
                OriginalTurnId = turn.TurnId,
                TokenCount = _tokenEstimator.EstimateTokens(turn.Content),
            }).ToList(),
            Diagnostics = new HistoryShapingDiagnostics
            {
                TotalTurns = fullHistory.Count,
                VerbatimTurns = history.Count,
                DroppedTurns = fullHistory.Count - history.Count,
                TotalTokensBefore = totalTokensBefore,
                TotalTokensAfter = totalTokensAfter,
                BudgetAvailable = budgetTokens,
            }
        };
    }

    private IReadOnlyList<ConversationTurn> AssembleSimple(
        IReadOnlyList<ConversationTurn> fullHistory,
        int budgetTokens)
    {
        ArgumentNullException.ThrowIfNull(fullHistory);

        if (fullHistory.Count == 0 || budgetTokens <= 0)
        {
            return [];
        }

        var result = new List<ConversationTurn>();
        var usedTokens = 0;

        for (var i = fullHistory.Count - 1; i >= 0; i--)
        {
            var turn = fullHistory[i];
            var tokens = _tokenEstimator.EstimateTokens(turn.Content);

            if (usedTokens + tokens > budgetTokens)
            {
                _logger.LogDebug(
                    "History budget exhausted at turn {IncludedTurns}/{TotalTurns} ({UsedTokens}/{BudgetTokens} tokens)",
                    fullHistory.Count - i,
                    fullHistory.Count,
                    usedTokens,
                    budgetTokens);
                break;
            }

            result.Insert(0, turn);
            usedTokens += tokens;
        }

        _logger.LogDebug(
            "History assembled: {Count}/{Total} turns, {Tokens} tokens used of {Budget} budget, recent verbatim target {RecentTurns}",
            result.Count,
            fullHistory.Count,
            usedTokens,
            budgetTokens,
            _historyConfig.RecentTurnsVerbatim);

        return result;
    }
}
