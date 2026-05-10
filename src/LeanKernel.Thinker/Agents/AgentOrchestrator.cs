using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Agents;

/// <summary>
/// Multi-agent orchestrator using MAF patterns:
/// - Simple queries: direct agent call
/// - Complex queries: Agent-as-Tool pattern (workers exposed as AIFunction tools
///   on a coordinator agent that the LLM can invoke as needed)
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly AgentFactory _agentFactory;
    private readonly PromptAssembler _promptAssembler;
    private readonly Dictionary<string, WorkerAgent> _workers = [];
    private readonly ILogger<AgentOrchestrator> _logger;

    /// <summary>
    /// Represents the agent orchestrator.
    /// </summary>
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

    /// <summary>
    /// Represents the process async.
    /// </summary>
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

        // Complex: use Agent-as-Tool pattern — coordinator agent with workers as tools
        _logger.LogInformation("Complex query — using Agent-as-Tool delegation");
        return await InvokeWithWorkersAsync(context, message.Content, ct);
    }

    /// <summary>
    /// Represents the delegate to worker async.
    /// </summary>
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

    /// <summary>
    /// Create a coordinator agent with workers exposed as AIFunction tools.
    /// The LLM decides which workers to invoke based on the task.
    /// </summary>
    private async Task<string> InvokeWithWorkersAsync(
        ConversationContext context,
        string query,
        CancellationToken ct)
    {
        try
        {
            var instructions = _promptAssembler.AssembleSystemMessage(context);

            // Build worker tools using Agent-as-Tool pattern
            var workerTools = BuildWorkerTools();
            var agent = _agentFactory.CreateInstrumentedAgent(instructions, workerTools);

            var response = await agent.RunAsync(query, cancellationToken: ct);
            return response.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent-as-Tool orchestration failed — falling back to direct");
            return await InvokeDirectAsync(context, query, ct);
        }
    }

    /// <summary>
    /// Build AIFunction tools from worker agents using Agent-as-Tool pattern.
    /// Each worker becomes a callable function that the coordinator agent can invoke.
    /// </summary>
    internal IReadOnlyList<AITool> BuildWorkerTools()
    {
        var tools = new List<AITool>();

        foreach (var (name, worker) in _workers)
        {
            // Create a dedicated agent for this worker
            var workerAgent = _agentFactory.CreateAgent(worker.Definition.SystemPrompt);

            // Convert agent to an AIFunction tool
            var tool = workerAgent.AsAIFunction(new AIFunctionFactoryOptions
            {
                Name = name,
                Description = worker.Definition.Description
            });

            tools.Add(tool);
        }

        _logger.LogDebug("Built {Count} worker tools via Agent-as-Tool", tools.Count);
        return tools;
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

/// <summary>
/// Represents the available task complexity values.
/// </summary>
public enum TaskComplexity
{
    /// <summary>
    /// Request can be handled directly without worker delegation.
    /// </summary>
    Simple,

    /// <summary>
    /// Request benefits from worker-agent delegation.
    /// </summary>
    Complex
}
