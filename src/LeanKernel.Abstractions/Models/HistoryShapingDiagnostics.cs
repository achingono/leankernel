namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Captures diagnostics for history shaping operations.
/// </summary>
public sealed record HistoryShapingDiagnostics
{
    /// <summary>
    /// Gets the total number of turns processed.
    /// </summary>
    public int TotalTurns { get; init; }

    /// <summary>
    /// Gets the count of turns kept verbatim.
    /// </summary>
    public int VerbatimTurns { get; init; }

    /// <summary>
    /// Gets the count of turns that were compacted.
    /// </summary>
    public int CompactedTurns { get; init; }

    /// <summary>
    /// Gets the count of turns that were summarized.
    /// </summary>
    public int SummarizedTurns { get; init; }

    /// <summary>
    /// Gets the count of turns that were dropped.
    /// </summary>
    public int DroppedTurns { get; init; }

    /// <summary>
    /// Gets the total tokens used before history shaping.
    /// </summary>
    public int TotalTokensBefore { get; init; }

    /// <summary>
    /// Gets the total tokens used after history shaping.
    /// </summary>
    public int TotalTokensAfter { get; init; }

    /// <summary>
    /// Gets the remaining available budget in tokens.
    /// </summary>
    public int BudgetAvailable { get; init; }

    /// <summary>
    /// Gets the list of compaction markers.
    /// </summary>
    public IReadOnlyList<CompactionMarker> Markers { get; init; } = [];
}
