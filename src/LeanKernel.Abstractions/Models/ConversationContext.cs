namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents the full context of a conversation.
/// </summary>
public sealed class ConversationContext
{
    /// <summary>
    /// Gets the system prompt used for the conversation.
    /// </summary>
    public required string SystemPrompt { get; init; }

    /// <summary>
    // Gets the session identifier, if any.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the conversation history.
    /// </summary>
    public IReadOnlyList<ConversationTurn> History { get; init; } = [];

    /// <summary>
    /// Gets the list of retrieved wiki facts.
    /// </summary>
    public IReadOnlyList<RetrievalCandidate> WikiFacts { get; init; } = [];

    /// <summary>
    /// Gets the list of retrieved knowledge.
    /// </summary>
    public IReadOnlyList<RetrievalCandidate> RetrievedKnowledge { get; init; } = [];

    /// <summary>
    /// Gets the identity context, if any.
    /// </summary>
    public IdentityContext? Identity { get; init; }

    /// <summary>
    /// Gets the onboarding result, if any.
    /// </summary>
    public OnboardingResult? Onboarding { get; init; }

    /// <summary>
    /// Gets the list of active tool names.
    /// </summary>
    public IReadOnlyList<string> ActiveToolNames { get; init; } = [];

    /// <summary>
    /// Gets the budget usage, if any.
    /// </summary>
    public ContextBudgetUsage? BudgetUsage { get; init; }

    /// <summary>
    /// Gets the admission log for the conversation.
    /// </summary>
    public IReadOnlyList<ContextAdmissionRecord> AdmissionLog { get; init; } = [];

    /// <summary>
    /// Gets the history shaping diagnostics, if any.
    /// </summary>
    public HistoryShapingDiagnostics? HistoryDiagnostics { get; init; }

    /// <summary>
    /// Gets the retrieval diagnostics, if any.
    /// </summary>
    public RetrievalDiagnostics? RetrievalDiagnostics { get; init; }
}
