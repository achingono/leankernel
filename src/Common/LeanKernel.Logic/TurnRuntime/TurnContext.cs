using Microsoft.Extensions.AI;

namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// Carries all state for a single turn through the pipeline stages.
/// Created at the start of each request and mutated by each stage.
/// </summary>
public sealed class TurnContext
{
    /// <summary>
    /// The request-scoped identity permit.
    /// </summary>
    public required IPermit Permit { get; init; }

    /// <summary>
    /// The inbound user message text.
    /// </summary>
    public required string UserMessage { get; init; }

    /// <summary>
    /// The conversation/session identifier.
    /// </summary>
    public required string ConversationId { get; init; }

    /// <summary>
    /// Candidate context items before admission gating.
    /// Populated by context-gathering stages, consumed by the gatekeeper.
    /// </summary>
    public List<ContextItem> Candidates { get; } = [];

    /// <summary>
    /// Context items that passed admission gating.
    /// Populated by the gatekeeper, consumed by prompt assembly.
    /// </summary>
    public List<ContextItem> Admitted { get; } = [];

    /// <summary>
    /// History messages after shaping/compaction.
    /// </summary>
    public List<ChatMessage> ShapedHistory { get; } = [];

    /// <summary>
    /// The final assembled prompt messages sent to the agent.
    /// </summary>
    public List<ChatMessage> Prompt { get; } = [];

    /// <summary>
    /// The agent's response after invocation.
    /// </summary>
    public ChatMessage? AgentResponse { get; set; }

    /// <summary>
    /// Token budget remaining after each admission decision.
    /// Starts at <see cref="Configuration.TurnPipelineSettings.MaxContextTokens"/>.
    /// </summary>
    public int RemainingBudget { get; set; }

    /// <summary>
    /// Admission trace for diagnostics. Records each decision.
    /// </summary>
    public List<AdmissionRecord> AdmissionTrace { get; } = [];

    /// <summary>
    /// Elapsed time for the full pipeline execution.
    /// </summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>
    /// Whether the pipeline should continue to a next round (for long-running tasks).
    /// </summary>
    public bool RequiresContinuation { get; set; }

    /// <summary>
    /// The termination reason if the pipeline stops.
    /// </summary>
    public string? TerminationReason { get; set; }
}

/// <summary>
/// A single candidate context item considered for admission into the prompt.
/// </summary>
public sealed class ContextItem
{
    /// <summary>
    /// Source category: "identity", "memory", "retrieval", "history", "system".
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// The text content of this context item.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Estimated token count for budget accounting.
    /// </summary>
    public int EstimatedTokens { get; init; }

    /// <summary>
    /// Relevance score (0.0 - 1.0). Higher = more relevant.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Optional metadata for diagnostics and filtering.
    /// </summary>
    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Records the gatekeeper's admission decision for a context item.
/// </summary>
public sealed class AdmissionRecord
{
    /// <summary>
    /// The source of the context item.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Whether the item was admitted.
    /// </summary>
    public required bool Admitted { get; init; }

    /// <summary>
    /// The reason for the decision (e.g., "budget_exhausted", "low_score", "admitted").
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Token cost of this item.
    /// </summary>
    public int TokenCost { get; init; }

    /// <summary>
    /// Remaining budget after this decision.
    /// </summary>
    public int RemainingBudget { get; init; }
}

/// <summary>
/// Result of a full pipeline execution.
/// </summary>
public sealed class TurnPipelineResult
{
    /// <summary>
    /// The agent's response message.
    /// </summary>
    public ChatMessage? Response { get; init; }

    /// <summary>
    /// Total pipeline execution time.
    /// </summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>
    /// Number of context items admitted.
    /// </summary>
    public int AdmittedCount { get; init; }

    /// <summary>
    /// Number of context items rejected.
    /// </summary>
    public int RejectedCount { get; init; }

    /// <summary>
    /// Whether the pipeline requires continuation.
    /// </summary>
    public bool RequiresContinuation { get; init; }

    /// <summary>
    /// The admission trace for diagnostics.
    /// </summary>
    public IReadOnlyList<AdmissionRecord> AdmissionTrace { get; init; } = [];
}