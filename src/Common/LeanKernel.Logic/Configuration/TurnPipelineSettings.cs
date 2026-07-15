namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configuration for the turn pipeline stages: budgets, history window,
/// compaction thresholds, and retrieval limits.
/// </summary>
public sealed class TurnPipelineSettings
{
    /// <summary>
    /// Maximum total tokens allowed in the assembled prompt context.
    /// Items exceeding this budget are rejected by the gatekeeper.
    /// </summary>
    public int MaxContextTokens { get; set; } = 8000;

    /// <summary>
    /// Maximum number of recent turns to keep verbatim in history.
    /// </summary>
    public int RecentTurnsVerbatim { get; set; } = 20;

    /// <summary>
    /// Maximum number of turns eligible for compaction (between verbatim window and dropped tail).
    /// </summary>
    public int CompactedTurnsMax { get; set; } = 50;

    /// <summary>
    /// Maximum number of turns eligible for summarization (beyond compacted window).
    /// </summary>
    public int SummarizedTurnsMax { get; set; } = 100;

    /// <summary>
    /// Whether to enable compaction of older turns.
    /// </summary>
    public bool EnableCompaction { get; set; } = false;

    /// <summary>
    /// Whether to enable LLM-based summarization of older turns.
    /// Requires a configured small-model client.
    /// </summary>
    public bool EnableSummarization { get; set; } = false;

    /// <summary>
    /// Maximum number of memory/retrieval candidates to admit.
    /// </summary>
    public int MaxRetrievalCandidates { get; set; } = 10;

    /// <summary>
    /// Minimum relevance score for a retrieval candidate to be admitted.
    /// </summary>
    public double MinRetrievalScore { get; set; } = 0.1;

    /// <summary>
    /// Token budget allocated to system/identity context.
    /// Remaining budget is shared between history and retrieval.
    /// </summary>
    public int SystemContextTokenBudget { get; set; } = 1000;

    /// <summary>
    /// Token budget allocated to retrieval/memory context.
    /// </summary>
    public int RetrievalTokenBudget { get; set; } = 3000;

    /// <summary>
    /// Maximum number of continuation rounds for long-running tasks.
    /// Set to 1 to disable continuation.
    /// </summary>
    public int MaxContinuationRounds { get; set; } = 1;

    /// <summary>
    /// Maximum wall-clock duration for the full pipeline including continuations.
    /// </summary>
    public TimeSpan MaxPipelineDuration { get; set; } = TimeSpan.FromMinutes(5);
}
