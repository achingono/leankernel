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
    ILogger<HistoryShaper> logger) : ITurnStage
{
    private readonly TurnPipelineSettings _settings = settings.Value;

    /// <inheritdoc />
    public string Name => "HistoryShaper";

    /// <inheritdoc />
    public Task ExecuteAsync(TurnContext context, CancellationToken cancellationToken = default)
    {
        // History is loaded by ChatHistoryProvider before the pipeline runs.
        // This stage shapes the loaded history into tiers.
        // The actual history loading is handled by the wiring stage (HistoryLoaderStage).
        // Here we only apply windowing logic to whatever is in ShapedHistory.

        if (context.ShapedHistory.Count == 0)
        {
            logger.LogDebug("No history to shape for conversation {ConversationId}.", context.ConversationId);
            return Task.CompletedTask;
        }

        var totalTurns = context.ShapedHistory.Count;
        var verbatimCount = Math.Min(_settings.RecentTurnsVerbatim, totalTurns);

        // Tier 1: Most recent turns — keep verbatim
        var verbatim = context.ShapedHistory
            .Skip(totalTurns - verbatimCount)
            .ToList();

        // Tier 2: Compacted turns — eligible for compaction if enabled
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

        // For Phase 03: keep verbatim only. Compaction and summarization are opt-in.
        context.ShapedHistory.Clear();

        if (_settings.EnableCompaction && compactedRange.Count > 0)
        {
            // Placeholder: in a full implementation, compactedRange would be
            // processed by ConversationCompactor to extract key facts.
            // For now, keep them as-is but mark them for later compaction.
            context.ShapedHistory.AddRange(compactedRange);
            logger.LogDebug(
                "History: {CompactedCount} turns in compacted window (compaction not yet implemented).",
                compactedRange.Count);
        }

        if (_settings.EnableSummarization && summarizedRange.Count > 0)
        {
            // Placeholder: summarizedRange would be summarized by LLM.
            // For now, keep them as-is.
            context.ShapedHistory.AddRange(summarizedRange);
            logger.LogDebug(
                "History: {SummarizedCount} turns in summarized window (summarization not yet implemented).",
                summarizedRange.Count);
        }

        // Always include verbatim window
        context.ShapedHistory.AddRange(verbatim);

        logger.LogDebug(
            "History shaped: {Total} total, {Verbatim} verbatim, {Compacted} compacted, {Summarized} summarized.",
            totalTurns, verbatim.Count, compactedRange.Count, summarizedRange.Count);

        return Task.CompletedTask;
    }
}
