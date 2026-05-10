using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Multi-agent orchestrator. Delegates sub-tasks to specialized
/// lean workers, each with a constrained context budget.
/// </summary>
public interface IAgentOrchestrator
{
    /// <summary>
    /// Processes a message by selecting the appropriate orchestration path for the current conversation context.
    /// </summary>
    Task<string> ProcessAsync(LeanKernelMessage message, ConversationContext context, CancellationToken ct);
    /// <summary>
    /// Delegates a task to a named worker using the supplied context budget.
    /// </summary>
    Task<string> DelegateToWorkerAsync(string workerName, string task, ContextBudget budget, CancellationToken ct);
}
