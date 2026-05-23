using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Routing;
using LeanKernel.Agents.Strategies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents.Orchestration;

/// <summary>
/// Coordinates coordinator-worker execution and falls back to the existing single-agent strategies when needed.
/// </summary>
public sealed class OrchestratedAgentStrategy(
    StaticAgentStrategy staticStrategy,
    RoutedAgentStrategy routedStrategy,
    IReadOnlyList<WorkerAgent> workers,
    OrchestrationDecider decider,
    IOptions<LeanKernelConfig> config,
    ILogger<OrchestratedAgentStrategy> logger) : IAgentStrategy
{
    private readonly StaticAgentStrategy _staticStrategy = staticStrategy ?? throw new ArgumentNullException(nameof(staticStrategy));
    private readonly RoutedAgentStrategy _routedStrategy = routedStrategy ?? throw new ArgumentNullException(nameof(routedStrategy));
    private readonly IReadOnlyList<WorkerAgent> _workers = workers ?? throw new ArgumentNullException(nameof(workers));
    private readonly OrchestrationDecider _decider = decider ?? throw new ArgumentNullException(nameof(decider));
    private readonly LeanKernelConfig _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
    private readonly ILogger<OrchestratedAgentStrategy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Gets the strategy name.
    /// </summary>
    public string Name => "orchestrated";

    /// <summary>
    /// Invokes the coordinator-worker orchestration path when warranted.
    /// </summary>
    /// <param name="context">The strategy context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The final assistant response.</returns>
    public async Task<string> InvokeAsync(AgentStrategyContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_config.Orchestration.Enabled)
        {
            return await InvokeFallbackAsync(context, ct).ConfigureAwait(false);
        }

        if (_workers.Count == 0)
        {
            _logger.LogInformation("Orchestration enabled but no workers are configured. Falling back to single-agent execution.");
            return await InvokeFallbackAsync(context, ct).ConfigureAwait(false);
        }

        var decision = _decider.Decide(context);
        if (!decision.ShouldOrchestrate)
        {
            _logger.LogDebug("Skipping orchestration for session {SessionId} turn {TurnId}: {Reason}", context.SessionId, context.TurnId, decision.Reason);
            return await InvokeFallbackAsync(context, ct).ConfigureAwait(false);
        }

        var contributions = new ConcurrentQueue<WorkerContribution>();

        try
        {
            using var concurrencyGate = new SemaphoreSlim(Math.Max(1, _config.Orchestration.MaxWorkerConcurrency));
            var workerTools = _workers
                .Select(worker => new WorkerAsToolAdapter(worker, context, 1, concurrencyGate, contributions).ToAITool())
                .ToArray();
            var coordinatorContext = new AgentStrategyContext
            {
                SessionId = context.SessionId,
                TurnId = context.TurnId,
                UserMessage = context.UserMessage,
                SystemMessage = BuildCoordinatorSystemMessage(context.SystemMessage, decision),
                History = context.History,
                Tools = workerTools,
                AvailableToolNames = _workers.Select(worker => worker.Name).ToArray()
            };
            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation(
                "Executing orchestration for session {SessionId} turn {TurnId} with {WorkerCount} workers",
                context.SessionId,
                context.TurnId,
                _workers.Count);

            var response = await InvokeCoordinatorAsync(coordinatorContext, ct).ConfigureAwait(false);
            stopwatch.Stop();

            context.ModelUsed = coordinatorContext.ModelUsed;
            context.TokensUsed = coordinatorContext.TokensUsed;
            context.RoutingDecision = coordinatorContext.RoutingDecision;
            context.QualityOutcome = coordinatorContext.QualityOutcome;
            context.QualityGateResult = coordinatorContext.QualityGateResult;
            context.OrchestrationResult = new OrchestrationResult
            {
                CoordinatorResponse = response,
                WorkerContributions = [.. contributions],
                TotalDuration = stopwatch.Elapsed,
                TotalWorkerInvocations = contributions.Count
            };

            return response;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!contributions.IsEmpty)
            {
                throw new InvalidOperationException(
                    "Orchestration failed after worker execution; fallback is blocked to avoid duplicate side effects.",
                    ex);
            }

            _logger.LogWarning(ex, "Orchestration failed for session {SessionId} turn {TurnId}. Falling back to single-agent execution.", context.SessionId, context.TurnId);
            context.OrchestrationResult = null;
            return await InvokeFallbackAsync(context, ct).ConfigureAwait(false);
        }
    }

    private Task<string> InvokeCoordinatorAsync(AgentStrategyContext context, CancellationToken ct)
        => _config.Routing.Enabled
            ? _routedStrategy.InvokeAsync(context, ct)
            : _staticStrategy.InvokeAsync(context, ct);

    private Task<string> InvokeFallbackAsync(AgentStrategyContext context, CancellationToken ct)
        => _config.Routing.Enabled
            ? _routedStrategy.InvokeAsync(context, ct)
            : _staticStrategy.InvokeAsync(context, ct);

    private string BuildCoordinatorSystemMessage(string systemMessage, OrchestrationDecision decision)
    {
        var builder = new StringBuilder(systemMessage.Trim());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("## Orchestration Mode");
        builder.Append("Reason: ")
            .AppendLine(decision.Reason);
        builder.AppendLine("You are coordinating specialized workers. Invoke worker tools only when they will improve the answer, then synthesize one final response for the user.");
        builder.AppendLine("Ignore any previously listed direct tools unless they are exposed through the worker tools available in this mode.");
        builder.AppendLine("Each worker receives only the task you provide, so be explicit and focused.");
        builder.AppendLine();
        builder.AppendLine("### Available Workers");

        foreach (var worker in _workers)
        {
            builder.Append("- ")
                .Append(worker.Name)
                .Append(": ")
                .AppendLine(worker.Description);
        }

        return builder.ToString().Trim();
    }
}
