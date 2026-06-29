namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents the response containing diagnostics about the context.
/// </summary>
public sealed record ContextDiagnosticsResponse
{
    /// <summary>
    /// Gets the session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets the turn identifier.
    /// </summary>
    public required string TurnId { get; init; }

    /// <summary>
    /// Gets the timestamp of the diagnostics.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Gets the list of admissions in the context.
    /// </summary>
    public IReadOnlyList<ContextAdmissionRecord> Admissions { get; init; } = [];

    /// <summary>
    /// Gets the total number of candidates considered during context assembly.
    /// </summary>
    public int TotalCandidatesConsidered { get; init; }

    /// <summary>
    /// Gets the total number of candidates admitted to the context.
    /// </summary>
    public int TotalAdmitted { get; init; }

    /// <summary>
    /// Gets the total number of candidates excluded from the context.
    /// </summary>
    public int TotalExcluded { get; init; }

    /// <summary>
    /// Gets the retrieval diagnostics, if any.
    /// </summary>
    public RetrievalDiagnostics? RetrievalDiagnostics { get; init; }
}

/// <summary>
/// Represents the response containing diagnostics about the budget.
/// </summary>
public sealed record BudgetDiagnosticsResponse
{
    /// <summary>
    /// Gets the session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets the turn identifier.
    /// </summary>
    public required string TurnId { get; init; }

    /// <summary>
    /// Gets the total budget in tokens.
    /// </summary>
    public int TotalBudgetTokens { get; init; }

    /// <summary>
    /// Gets the usable budget in tokens.
    /// </summary>
    public int UsableBudgetTokens { get; init; }

    /// <summary>
    /// Gets the response headroom ratio.
    /// </summary>
    public double ResponseHeadroomRatio { get; init; }

    /// <summary>
    /// Gets the usage details for context.
    /// </summary>
    public required ContextBudgetUsage Usage { get; init; }

    /// <summary>
    /// Gets the budget details for the system prompt.
    /// </summary>
    public required BudgetCategoryDetail SystemPrompt { get; init; }

    /// <summary>
    /// Gets the budget details for wiki facts.
    /// </summary>
    public required BudgetCategoryDetail WikiFacts { get; init; }

    /// <summary>
    /// Gets the budget details for retrieval.
    /// </summary>
    public required BudgetCategoryDetail Retrieval { get; init; }

    /// <summary>
    /// Gets the budget details for the conversation.
    /// </summary>
    public required BudgetCategoryDetail Conversation { get; init; }

    /// <summary>
    /// Gets the budget details for tools.
    /// </summary>
    public required BudgetCategoryDetail Tools { get; init; }
}

/// <summary>
/// Represents the details for a budget category.
/// </summary>
public sealed record BudgetCategoryDetail
{
    /// <summary>
    /// Gets the allocated tokens for the category.
    /// </summary>
    public int Allocated { get; init; }

    /// <summary>
    /// Gets the used tokens for the category.
    /// </summary>
    public int Used { get; init; }

    /// <summary>
    /// Gets the utilization percentage for the category.
    /// </summary>
    public double UtilizationPercent => Allocated > 0 ? (double)Used / Allocated * 100 : 0;
}

/// <summary>
/// Represents the response containing diagnostics about history shaping.
/// </summary>
public sealed record HistoryDiagnosticsResponse
{
    /// <summary>
    /// Gets the session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets the turn identifier.
    /// </summary>
    public required string TurnId { get; init; }

    /// <summary>
    /// Gets the shaping details, if any.
    /// </summary>
    public HistoryShapingDiagnostics? Shaping { get; init; }

    /// <summary>
    /// Gets the number of verbatim turns.
    /// </summary>
    public int VerbatimTurns { get; init; }

    /// <summary>
    /// Gets the number of compacted turns.
    /// </summary>
    public int CompactedTurns { get; init; }

    /// <summary>
    /// Gets the number of summarized turns.
    /// </summary>
    public int SummarizedTurns { get; init; }

    /// <summary>
    /// Gets the number of dropped turns.
    /// </summary>
    public int DroppedTurns { get; init; }

    /// <summary>
    /// Gets the tokens saved through history shaping.
    /// </summary>
    public int TokensSaved { get; init; }
}
