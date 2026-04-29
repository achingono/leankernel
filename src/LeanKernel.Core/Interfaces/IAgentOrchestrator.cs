using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Multi-agent orchestrator. Delegates sub-tasks to specialized
/// lean workers, each with a constrained context budget.
/// </summary>
public interface IAgentOrchestrator
{
    Task<string> ProcessAsync(LeanKernelMessage message, ConversationContext context, CancellationToken ct);
    Task<string> DelegateToWorkerAsync(string workerName, string task, ContextBudget budget, CancellationToken ct);
}
