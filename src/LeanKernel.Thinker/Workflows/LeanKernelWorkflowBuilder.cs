using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using LeanKernel.Thinker.Agents;

namespace LeanKernel.Thinker.Workflows;

/// <summary>
/// Builds a MAF Workflow graph from LeanKernel's worker agents.
/// The workflow exposes workers as parallel execution nodes in a
/// fan-out/fan-in pattern using MAF's superstep BSP model.
///
/// Graph structure:
///   coordinator → [research, code, schedule] → aggregator → output
///
/// Workers execute in parallel within one superstep when the coordinator
/// delegates to multiple workers simultaneously.
/// </summary>
public sealed class LeanKernelWorkflowBuilder
{
    private readonly AgentFactory _agentFactory;
    private readonly IEnumerable<WorkerAgent> _workers;
    private readonly ILogger<LeanKernelWorkflowBuilder> _logger;

    /// <summary>
    /// Represents the lean kernel workflow builder.
    /// </summary>
    public LeanKernelWorkflowBuilder(
        AgentFactory agentFactory,
        IEnumerable<WorkerAgent> workers,
        ILogger<LeanKernelWorkflowBuilder> logger)
    {
        _agentFactory = agentFactory;
        _workers = workers;
        _logger = logger;
    }

    /// <summary>
    /// Build the workflow as an AIAgent. The returned agent can be called
    /// with RunAsync() like any other agent — the workflow handles parallel
    /// worker execution internally.
    /// </summary>
    public AIAgent BuildAsAgent()
    {
        var coordinatorInstructions =
            "You are a task coordinator. Analyze the request and delegate to " +
            "the appropriate specialist workers. Combine their results into a " +
            "coherent response. Available workers: " +
            string.Join(", ", _workers.Select(w => $"{w.Definition.Name} ({w.Definition.Description})"));

        // Create worker tools using Agent-as-Tool pattern
        var workerTools = new List<AITool>();
        foreach (var worker in _workers)
        {
            var workerAgent = _agentFactory.CreateAgent(worker.Definition.SystemPrompt);
            var tool = workerAgent.AsAIFunction(new AIFunctionFactoryOptions
            {
                Name = worker.Definition.Name,
                Description = worker.Definition.Description
            });
            workerTools.Add(tool);
        }

        // Create a coordinator agent with workers as tools
        var coordinator = _agentFactory.CreateInstrumentedAgent(
            coordinatorInstructions,
            workerTools);

        _logger.LogInformation(
            "Built workflow agent with {WorkerCount} worker tools",
            workerTools.Count);

        return coordinator;
    }
}
