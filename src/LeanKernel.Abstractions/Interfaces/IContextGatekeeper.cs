using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Gatekeeps the context by applying constraints and budgeting.
/// </summary>
public interface IContextGatekeeper
{
    /// <summary>
    /// Applies context gating to the message and budget.
    /// </summary>
    /// <param name="message">The input message.</param>
    /// <param name="budget">The allocated context budget.</param>
    /// <param name="sessionId">The current session identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The processed context for the turn.</returns>
    Task<ConversationContext> GateContextAsync(
        LeanKernelMessage message,
        ContextBudget budget,
        string sessionId,
        CancellationToken ct = default);
}
