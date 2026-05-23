using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Strategies;
using Microsoft.Extensions.AI;

namespace LeanKernel.Agents.Orchestration;

/// <summary>
/// Adapts a <see cref="WorkerAgent"/> into a coordinator-callable AI tool.
/// </summary>
public sealed class WorkerAsToolAdapter
{
    private readonly WorkerAgent _workerAgent;
    private readonly AgentStrategyContext _coordinatorContext;
    private readonly int _currentDepth;
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly ConcurrentQueue<WorkerContribution> _contributions;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerAsToolAdapter"/> class.
    /// </summary>
    /// <param name="workerAgent">The worker agent.</param>
    /// <param name="coordinatorContext">The parent coordinator context.</param>
    /// <param name="currentDepth">The current orchestration depth.</param>
    /// <param name="concurrencyGate">The concurrency limiter shared by the orchestration run.</param>
    /// <param name="contributions">The concurrency-safe contribution sink.</param>
    public WorkerAsToolAdapter(
        WorkerAgent workerAgent,
        AgentStrategyContext coordinatorContext,
        int currentDepth,
        SemaphoreSlim concurrencyGate,
        ConcurrentQueue<WorkerContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(workerAgent);
        ArgumentNullException.ThrowIfNull(coordinatorContext);
        ArgumentNullException.ThrowIfNull(concurrencyGate);
        ArgumentNullException.ThrowIfNull(contributions);

        _workerAgent = workerAgent;
        _coordinatorContext = coordinatorContext;
        _currentDepth = currentDepth;
        _concurrencyGate = concurrencyGate;
        _contributions = contributions;
    }

    /// <summary>
    /// Creates the coordinator-facing AI tool.
    /// </summary>
    /// <returns>The worker as an invocable tool.</returns>
    public AITool ToAITool() => new WorkerToolFunction(this);

    private async ValueTask<object?> InvokeAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var task = arguments.TryGetValue("task", out var taskValue)
            ? taskValue?.ToString() ?? string.Empty
            : string.Empty;

        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var contribution = await _workerAgent.ExecuteTaskAsync(
                _coordinatorContext,
                task,
                _currentDepth + 1,
                cancellationToken).ConfigureAwait(false);
            _contributions.Enqueue(contribution);

            return contribution.Success
                ? contribution.Response
                : $"{_workerAgent.Name} failed: {contribution.Error}";
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    private sealed class WorkerToolFunction(WorkerAsToolAdapter adapter) : AIFunction
    {
        private readonly WorkerAsToolAdapter _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        private readonly JsonElement _jsonSchema = CreateJsonSchema();

        public override string Name => _adapter._workerAgent.Name;

        public override string Description => $"Delegates work to worker '{_adapter._workerAgent.Name}'. {_adapter._workerAgent.Description}";

        public override JsonElement JsonSchema => _jsonSchema;

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
            => _adapter.InvokeAsync(arguments, cancellationToken);

        private static JsonElement CreateJsonSchema()
        {
            JsonObject schema = new()
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["task"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Focused task description for the worker."
                    }
                },
                ["required"] = new JsonArray("task"),
                ["additionalProperties"] = false
            };

            return JsonSerializer.SerializeToElement(schema);
        }
    }
}
