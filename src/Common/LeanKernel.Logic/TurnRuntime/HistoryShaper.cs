using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// Deterministic history shaper. Selects the eligible window from persisted turns,
/// applies tiered compaction (verbatim / compacted / summarized / dropped),
/// and populates <see cref="TurnContext.ShapedHistory"/>.
/// </summary>
public sealed class HistoryShaper(
    IOptions<TurnPipelineSettings> settings,
    IHistorySummarizer historySummarizer,
    IHistoryCompactor historyCompactor,
    ILogger<HistoryShaper> logger) : ITurnStage
{
    private readonly TurnPipelineSettings _settings = settings.Value;

    /// <inheritdoc />
    public string Name => "HistoryShaper";

    /// <inheritdoc />
    public async Task ExecuteAsync(TurnContext context, CancellationToken cancellationToken = default)
    {
        // History is loaded by ChatHistoryProvider before the pipeline runs.
        // This stage shapes the loaded history into tiers.
        // The actual history loading is handled by the wiring stage (HistoryLoaderStage).
        // Here we only apply windowing logic to whatever is in ShapedHistory.
        if (context.ShapedHistory.Count == 0)
        {
            logger.LogDebug("No history to shape for conversation {ConversationId}.", context.ConversationId);
            return;
        }

        var totalTurns = context.ShapedHistory.Count;
        var verbatimCount = Math.Min(_settings.RecentTurnsVerbatim, totalTurns);

        // Tier 1: Most recent turns — keep verbatim
        var verbatim = context.ShapedHistory
            .Skip(totalTurns - verbatimCount)
            .ToList();

        // Tier 2: Compacted turns — eligible for embedding-based compaction if enabled
        var compactedStart = Math.Max(0, totalTurns - verbatimCount - _settings.CompactedTurnsMax);
        var compactedEnd = Math.Max(0, totalTurns - verbatimCount);
        var compactedRange = context.ShapedHistory
            .Skip(compactedStart)
            .Take(compactedEnd - compactedStart)
            .ToList();

        // Tier 3: Summarized turns — eligible for summarization if enabled
        var summarizedStart = Math.Max(0, compactedStart - _settings.SummarizedTurnsMax);
        var summarizedRange = context.ShapedHistory
            .Skip(summarizedStart)
            .Take(compactedStart - summarizedStart)
            .ToList();

        context.ShapedHistory.Clear();

        // Tier 3: LLM-based abstractive summarization (oldest window)
        if (_settings.EnableSummarization && summarizedRange.Count > 0)
        {
            var summary = await historySummarizer
                .SummarizeAsync(summarizedRange, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(summary))
            {
                context.ShapedHistory.Add(new ChatMessage(
                    ChatRole.System,
                    $"Summary of earlier conversation turns:\n{summary}"));
                logger.LogDebug(
                    "History: summarized {SummarizedCount} turns into one summary message.",
                    summarizedRange.Count);
            }
            else
            {
                context.ShapedHistory.AddRange(summarizedRange);
                logger.LogWarning(
                    "History summarization unavailable; kept {SummarizedCount} turns verbatim.",
                    summarizedRange.Count);
            }
        }

        // Tier 2: Embedding-based extractive compaction (middle window)
        if (_settings.EnableCompaction && compactedRange.Count > 0)
        {
            var compacted = await historyCompactor
                .CompactAsync(compactedRange, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(compacted))
            {
                context.ShapedHistory.Add(new ChatMessage(
                    ChatRole.User,
                    $"Historical context (compacted, untrusted):\n{compacted}"));
                logger.LogDebug(
                    "History: compacted {CompactedCount} turns into extractive summary.",
                    compactedRange.Count);
            }
            else
            {
                context.ShapedHistory.AddRange(compactedRange);
                logger.LogWarning(
                    "History compaction unavailable; kept {CompactedCount} turns verbatim.",
                    compactedRange.Count);
            }
        }

        // Tier 1: Always include verbatim window
        context.ShapedHistory.AddRange(verbatim);

        logger.LogDebug(
            "History shaped: {Total} total, {Verbatim} verbatim, {Compacted} compacted, {Summarized} summarized.",
            totalTurns, verbatim.Count, compactedRange.Count, summarizedRange.Count);
    }
}