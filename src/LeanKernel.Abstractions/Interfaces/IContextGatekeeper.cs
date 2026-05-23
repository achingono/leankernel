using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

public interface IContextGatekeeper
{
    Task<ConversationContext> GateContextAsync(
        LeanKernelMessage message,
        ContextBudget budget,
        string sessionId,
        CancellationToken ct = default);
}
