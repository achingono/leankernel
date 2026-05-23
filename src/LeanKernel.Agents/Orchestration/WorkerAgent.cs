using System.Diagnostics;
using System.Text;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Strategies;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents.Orchestration;

/// <summary>
/// Represents a scoped worker agent with its own model, prompt, and tool allowlist.
/// </summary>
public sealed class WorkerAgent
{
    private readonly AgentFactory _agentFactory;
    private readonly IToolRegistry _toolRegistry;
    private readonly OrchestrationConfig _orchestration;
    private readonly ILogger<WorkerAgent> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerAgent"/> class.
    /// </summary>
    /// <param name="definition">The worker definition.</param>
    /// <param name="agentFactory">The chat-client factory.</param>
    /// <param name="toolRegistry">The tool registry.</param>
    /// <param name="config">The LeanKernel configuration.</param>
    /// <param name="logger">The logger.</param>
    public WorkerAgent(
        WorkerDefinition definition,
        AgentFactory agentFactory,
        IToolRegistry toolRegistry,
        IOptions<LeanKernelConfig> config,
        ILogger<WorkerAgent> logger)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(agentFactory);
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        Definition = definition;
        _agentFactory = agentFactory;
        _toolRegistry = toolRegistry;
        _orchestration = config.Value.Orchestration;
        _logger = logger;
    }

    /// <summary>
    /// Gets the worker definition.
    /// </summary>
    public WorkerDefinition Definition { get; }

    /// <summary>
    /// Gets the worker name.
    /// </summary>
    public string Name => Definition.Name;

    /// <summary>
    /// Gets the worker description.
    /// </summary>
    public string Description => Definition.Description;

    /// <summary>
    /// Executes a delegated task within the worker's configured scope.
    /// </summary>
    /// <param name="coordinatorContext">The parent coordinator context.</param>
    /// <param name="task">The delegated task.</param>
    /// <param name="depth">The orchestration depth for this worker invocation.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The worker contribution.</returns>
    public async Task<WorkerContribution> ExecuteTaskAsync(
        AgentStrategyContext coordinatorContext,
        string task,
        int depth,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(coordinatorContext);

        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(task))
        {
            stopwatch.Stop();
            return CreateFailure(string.Empty, stopwatch.Elapsed, $"Worker '{Name}' requires a task description.");
        }

        if (depth > _orchestration.MaxOrchestrationDepth)
        {
            stopwatch.Stop();
            return CreateFailure(task, stopwatch.Elapsed, $"Orchestration depth {depth} exceeds max depth {_orchestration.MaxOrchestrationDepth}.");
        }

        var workerTools = BuildWorkerTools();
        var workerToolNames = workerTools.Select(tool => tool.Name).ToArray();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(_orchestration.WorkerTimeout);

        try
        {
            var workerContext = new AgentStrategyContext
            {
                SessionId = coordinatorContext.SessionId,
                TurnId = coordinatorContext.TurnId,
                UserMessage = task,
                SystemMessage = BuildSystemMessage(workerToolNames),
                History = [],
                Tools = workerTools,
                AvailableToolNames = workerToolNames,
            };
            var chatClient = _agentFactory.GetChatClientForModel(Definition.Model);
            var messages = AgentInvocationBuilder.BuildMessages(workerContext);
            var options = AgentInvocationBuilder.BuildOptions(workerContext);

            _logger.LogInformation(
                "Invoking worker {Worker} with model {Model} at depth {Depth} and {ToolCount} tools",
                Name,
                Definition.Model,
                depth,
                workerTools.Count);

            var response = await chatClient.GetResponseAsync(messages, options, timeoutSource.Token).ConfigureAwait(false);
            stopwatch.Stop();

            return new WorkerContribution
            {
                WorkerName = Name,
                Task = task,
                Response = response.Text ?? string.Empty,
                Duration = stopwatch.Elapsed,
                Success = true
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            return CreateFailure(task, stopwatch.Elapsed, $"Worker '{Name}' timed out after {_orchestration.WorkerTimeout}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Worker {Worker} failed", Name);
            return CreateFailure(task, stopwatch.Elapsed, ex.Message);
        }
    }

    private IReadOnlyList<AITool> BuildWorkerTools()
    {
        var allowedToolNames = NormalizeValues(Definition.AllowedTools);
        var allowedCategories = NormalizeValues(Definition.AllowedCategories);

        if (allowedToolNames.Count == 0 && allowedCategories.Count == 0)
        {
            return [];
        }

        var visibleTools = _toolRegistry.GetVisibleTools(new ToolVisibilityContext
        {
            AgentRole = Definition.Scope,
            AllowedToolNames = allowedToolNames,
            AllowedCategories = allowedCategories
        });

        return visibleTools
            .Select(ToolDefinitionAIToolAdapter.Create)
            .ToArray();
    }

    private string BuildSystemMessage(IReadOnlyList<string> toolNames)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(Definition.SystemPrompt))
        {
            builder.AppendLine(Definition.SystemPrompt.Trim());
        }
        else
        {
            builder.AppendLine($"You are {Definition.Name}. {Definition.Description}");
        }

        builder.AppendLine("Complete only the delegated task for the coordinator.");
        builder.AppendLine("Do not attempt to coordinate other workers or invent additional capabilities.");

        if (!string.IsNullOrWhiteSpace(Definition.Scope))
        {
            builder.Append("Restrict your work to scope '")
                .Append(Definition.Scope)
                .AppendLine("'.");
        }

        builder.AppendLine(toolNames.Count > 0
            ? $"You may use only these tools: {string.Join(", ", toolNames)}."
            : "You do not have access to any tools.");

        return builder.ToString().Trim();
    }

    private WorkerContribution CreateFailure(string task, TimeSpan duration, string error)
        => new()
        {
            WorkerName = Name,
            Task = task,
            Response = string.Empty,
            Duration = duration,
            Success = false,
            Error = error
        };

    private static IReadOnlyList<string> NormalizeValues(IEnumerable<string>? values)
        => values?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
}
