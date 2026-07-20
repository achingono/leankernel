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
