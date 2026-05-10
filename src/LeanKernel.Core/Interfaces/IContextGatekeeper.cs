using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// The Archivist's core contract — decides what context to include
/// in the LLM prompt using a deny-by-default gating strategy.
/// </summary>
public interface IContextGatekeeper
{
    /// <summary>
    /// Build a minimal, budget-aware context window for the given query.
    /// Starts from nothing and only adds LeanKernels that earn their place.
    /// </summary>
    /// <param name="query">The inbound message to build context for.</param>
    /// <param name="budget">The token budget allocations for each context category.</param>
    /// <param name="sessionId">The conversation session identifier.</param>
    /// <param name="ct">A token used to cancel context retrieval.</param>
    /// <returns>The selected conversation context.</returns>
    Task<ConversationContext> GateContextAsync(
        LeanKernelMessage query,
        ContextBudget budget,
        string sessionId,
        CancellationToken ct);

    /// <summary>
    /// Build a minimal, budget-aware context window with agent-scoped knowledge access.
    /// </summary>
    /// <param name="query">The inbound message to build context for.</param>
    /// <param name="budget">The token budget allocations for each context category.</param>
    /// <param name="sessionId">The conversation session identifier.</param>
    /// <param name="agentKnowledgeTags">The knowledge tags this agent is allowed to retrieve.</param>
    /// <param name="ct">A token used to cancel context retrieval.</param>
    /// <returns>The selected conversation context.</returns>
    Task<ConversationContext> GateContextAsync(
        LeanKernelMessage query,
        ContextBudget budget,
        string sessionId,
        IReadOnlyList<string> agentKnowledgeTags,
        CancellationToken ct);
}
