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
    Task<ConversationContext> GateContextAsync(
        LeanKernelMessage query,
        ContextBudget budget,
        string sessionId,
        CancellationToken ct);
}
