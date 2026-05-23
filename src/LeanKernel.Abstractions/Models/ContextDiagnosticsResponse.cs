namespace LeanKernel.Abstractions.Models;

public sealed record ContextDiagnosticsResponse
{
    public required string SessionId { get; init; }
    public required string TurnId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public IReadOnlyList<ContextAdmissionRecord> Admissions { get; init; } = [];
    public int TotalCandidatesConsidered { get; init; }
    public int TotalAdmitted { get; init; }
    public int TotalExcluded { get; init; }
    public RetrievalDiagnostics? RetrievalDiagnostics { get; init; }
}

public sealed record BudgetDiagnosticsResponse
{
    public required string SessionId { get; init; }
    public required string TurnId { get; init; }
    public int TotalBudgetTokens { get; init; }
    public int UsableBudgetTokens { get; init; }
    public double ResponseHeadroomRatio { get; init; }
    public required ContextBudgetUsage Usage { get; init; }
    public required BudgetCategoryDetail SystemPrompt { get; init; }
    public required BudgetCategoryDetail WikiFacts { get; init; }
    public required BudgetCategoryDetail Retrieval { get; init; }
    public required BudgetCategoryDetail Conversation { get; init; }
    public required BudgetCategoryDetail Tools { get; init; }
}

public sealed record BudgetCategoryDetail
{
    public int Allocated { get; init; }
    public int Used { get; init; }
    public double UtilizationPercent => Allocated > 0 ? (double)Used / Allocated * 100 : 0;
}

public sealed record HistoryDiagnosticsResponse
{
    public required string SessionId { get; init; }
    public required string TurnId { get; init; }
    public HistoryShapingDiagnostics? Shaping { get; init; }
    public int VerbatimTurns { get; init; }
    public int CompactedTurns { get; init; }
    public int SummarizedTurns { get; init; }
    public int DroppedTurns { get; init; }
    public int TokensSaved { get; init; }
}
