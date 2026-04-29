using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Agents;

/// <summary>
/// Multi-agent orchestrator. Analyzes the complexity of a query and either
/// handles it directly or delegates to specialized worker agents, each with
/// constrained context budgets.
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly AgentFactory _agentFactory;
    private readonly PromptAssembler _promptAssembler;
    private readonly Dictionary<string, WorkerAgent> _workers = [];
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        AgentFactory agentFactory,
        PromptAssembler promptAssembler,
        IEnumerable<WorkerAgent> workers,
        ILogger<AgentOrchestrator> logger)
    {
        _agentFactory = agentFactory;
        _promptAssembler = promptAssembler;
        foreach (var worker in workers)
            _workers[worker.Definition.Name] = worker;
        _logger = logger;
    }

    public async Task<string> ProcessAsync(
        LeanKernelMessage message,
        ConversationContext context,
        CancellationToken ct)
    {
        var complexity = AnalyzeComplexity(message.Content);

        if (complexity == TaskComplexity.Simple)
        {
            _logger.LogDebug("Simple query — handling directly");
            return await InvokeDirectAsync(context, message.Content, ct);
        }

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
            var instructions = _promptAssembler.AssembleSystemMessage(context);
            var agent = _agentFactory.CreateAgent(instructions);
            var response = await agent.RunAsync(query, cancellationToken: ct);
            return response.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct LLM invocation failed");
            return "I encountered an error processing your request.";
        }
    }

    /// <summary>
    /// Simple heuristic complexity analysis.
    /// </summary>
    internal static TaskComplexity AnalyzeComplexity(string query)
    {
        var lower = query.ToLowerInvariant();

        if (lower.Contains(" and then ") || lower.Contains(" after that "))
            return TaskComplexity.Complex;

        if (lower.Contains("research") || lower.Contains("compare") ||
            lower.Contains("analyze") || lower.Contains("investigate"))
            return TaskComplexity.Complex;

        if (lower.Contains("write code") || lower.Contains("create a program") ||
            lower.Contains("implement"))
            return TaskComplexity.Complex;

        if (query.Length > 500)
            return TaskComplexity.Complex;

        return TaskComplexity.Simple;
    }

    /// <summary>
    /// Decompose a complex task into worker assignments.
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
