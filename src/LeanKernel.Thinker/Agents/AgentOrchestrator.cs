using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.SemanticKernel;

namespace LeanKernel.Thinker.Agents;

/// <summary>
/// Multi-agent orchestrator. Analyzes the complexity of a query and either
/// handles it directly or delegates to specialized worker agents, each with
/// constrained context budgets.
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly KernelFactory _kernelFactory;
    private readonly Dictionary<string, WorkerAgent> _workers = [];
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        KernelFactory kernelFactory,
        IEnumerable<WorkerAgent> workers,
        ILogger<AgentOrchestrator> logger)
    {
        _kernelFactory = kernelFactory;
        foreach (var worker in workers)
            _workers[worker.Definition.Name] = worker;
        _logger = logger;
    }

    public async Task<string> ProcessAsync(
        LeanKernelMessage message,
        ConversationContext context,
        CancellationToken ct)
    {
        // Analyze complexity: if simple, handle directly; if complex, delegate
        var complexity = AnalyzeComplexity(message.Content);

        if (complexity == TaskComplexity.Simple)
        {
            _logger.LogDebug("Simple query — handling directly");
            return await InvokeDirectAsync(context, message.Content, ct);
        }

        // Complex: decompose into sub-tasks and delegate to workers
        _logger.LogInformation("Complex query detected — orchestrating workers");
        var plan = DecomposeTask(message.Content);

        var results = new List<string>();
        foreach (var (workerName, subTask) in plan)
        {
            if (_workers.TryGetValue(workerName, out var worker))
            {
                var subBudget = ContextBudget.FromModelWindow(
                    worker.Definition.MaxContextTokens);
                var result = await worker.ExecuteAsync(subTask, subBudget, ct);
                results.Add($"[{workerName}] {result}");
            }
            else
            {
                _logger.LogWarning("Unknown worker: {Worker}", workerName);
                results.Add($"[{workerName}] Worker not available");
            }
        }

        // Synthesize results
        return results.Count > 0
            ? string.Join("\n\n", results)
            : await InvokeDirectAsync(context, message.Content, ct);
    }

    public async Task<string> DelegateToWorkerAsync(
        string workerName,
        string task,
        ContextBudget budget,
        CancellationToken ct)
    {
        if (!_workers.TryGetValue(workerName, out var worker))
            return $"Error: worker '{workerName}' not found";

        return await worker.ExecuteAsync(task, budget, ct);
    }

    private async Task<string> InvokeDirectAsync(
        ConversationContext context,
        string query,
        CancellationToken ct)
    {
        try
        {
            var kernel = _kernelFactory.Build();
            return await LiteLlmConnector.InvokeAsync(kernel, context, query, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct LLM invocation failed");
            return "I encountered an error processing your request.";
        }
    }

    /// <summary>
    /// Simple heuristic complexity analysis.
    /// Future: use a lightweight classifier model.
    /// </summary>
    internal static TaskComplexity AnalyzeComplexity(string query)
    {
        var lower = query.ToLowerInvariant();

        // Multi-step indicators
        if (lower.Contains(" and then ") || lower.Contains(" after that "))
            return TaskComplexity.Complex;

        // Research indicators
        if (lower.Contains("research") || lower.Contains("compare") ||
            lower.Contains("analyze") || lower.Contains("investigate"))
            return TaskComplexity.Complex;

        // Code generation
        if (lower.Contains("write code") || lower.Contains("create a program") ||
            lower.Contains("implement"))
            return TaskComplexity.Complex;

        // Length heuristic: very long queries are likely complex
        if (query.Length > 500)
            return TaskComplexity.Complex;

        return TaskComplexity.Simple;
    }

    /// <summary>
    /// Decompose a complex task into worker assignments.
    /// Returns (workerName, subTask) pairs.
    /// </summary>
    internal static List<(string Worker, string Task)> DecomposeTask(string query)
    {
        var plan = new List<(string, string)>();
        var lower = query.ToLowerInvariant();

        if (lower.Contains("research") || lower.Contains("search") || lower.Contains("find"))
            plan.Add(("research", query));

        if (lower.Contains("code") || lower.Contains("program") || lower.Contains("implement"))
            plan.Add(("code", query));

        if (lower.Contains("schedule") || lower.Contains("remind") || lower.Contains("calendar"))
            plan.Add(("schedule", query));

        // Default: if no specific worker matched, send to research
        if (plan.Count == 0)
            plan.Add(("research", query));

        return plan;
    }
}

public enum TaskComplexity
{
    Simple,
    Complex
}
