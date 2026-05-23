namespace LeanKernel.Abstractions.Models;

public sealed record HistoryShapingDiagnostics
{
    public int TotalTurns { get; init; }
    public int VerbatimTurns { get; init; }
    public int CompactedTurns { get; init; }
    public int SummarizedTurns { get; init; }
    public int DroppedTurns { get; init; }
    public int TotalTokensBefore { get; init; }
    public int TotalTokensAfter { get; init; }
    public int BudgetAvailable { get; init; }
    public IReadOnlyList<CompactionMarker> Markers { get; init; } = [];
}
