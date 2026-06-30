using System.Diagnostics;
using System.Text;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Orchestration;
using LeanKernel.Agents.Routing;
using LeanKernel.Agents.Strategies;
using LeanKernel.Agents.ToolSelection;
using LeanKernel.Context;
using LeanKernel.Context.Identity;
using LeanKernel.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents;

/// <summary>
/// The canonical turn pipeline:
/// 1. Persist user turn
/// 2. Gate context (deny-by-default)
/// 3. Assemble prompt
/// 4. Invoke agent strategy
/// 5. Apply response enhancement (optional)
/// 6. Persist assistant turn + emit TurnEvent
/// </summary>
public sealed class TurnPipeline : ITurnPipeline
{
    private static readonly HashSet<string> CoreToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "web_search",
        "web_fetch",
        "http_request",
        "wiki_search",
        "wiki_read",
        "file_read",
        "file_search",
        "file_write",
        "file_edit",
        "browser_run_task",
        "browser_get_run",
        "browser_get_artifact",
        "browser_cancel_run"
    };

    private static readonly HashSet<string> BrowserBuiltInToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "browser_run_task",
        "browser_get_run",
        "browser_get_artifact",
        "browser_cancel_run"
    };

    private readonly IContextGatekeeper _gatekeeper;
    private readonly ISessionStore _sessions;
    private readonly IAgentStrategy _strategy;
    private readonly PromptAssembler _promptAssembler;
    private readonly IToolRegistry _toolRegistry;
    private readonly IResponseEnhancer? _responseEnhancer;
    private readonly IdentityUpdateProjector? _identityUpdateProjector;
    private readonly ITurnEventSink? _turnEventSink;
    private readonly IContextDiagnosticsService? _contextDiagnosticsService;
    private readonly DiagnosticsCollector? _diagnosticsCollector;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<TurnPipeline> _logger;
    private readonly IGracefulDegradationPolicy? _gracefulDegradationPolicy;
    private readonly ISpendGuardService? _spendGuardService;
    private readonly ISpendTracker? _spendTracker;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly LeanKernelMetrics? _metrics;
    private readonly IProviderHealthTracker? _providerHealthTracker;
    private readonly TaskComplexityScorer? _taskComplexityScorer;
    private readonly PolicyModelSelector? _policyModelSelector;
    private readonly IToolSelector _toolSelector;

    public TurnPipeline(
        IContextGatekeeper gatekeeper,
        ISessionStore sessions,
        IAgentStrategy strategy,
        PromptAssembler promptAssembler,
        IToolRegistry toolRegistry,
        IOptions<LeanKernelConfig> config,
        ILogger<TurnPipeline> logger,
        IResponseEnhancer? responseEnhancer = null,
        ITurnEventSink? turnEventSink = null,
        IContextDiagnosticsService? contextDiagnosticsService = null,
        DiagnosticsCollector? diagnosticsCollector = null,
        IdentityUpdateProjector? identityUpdateProjector = null,
        IGracefulDegradationPolicy? gracefulDegradationPolicy = null,
        ISpendGuardService? spendGuardService = null,
        ISpendTracker? spendTracker = null,
        ITokenEstimator? tokenEstimator = null,
        LeanKernelMetrics? metrics = null,
        IProviderHealthTracker? providerHealthTracker = null,
        TaskComplexityScorer? taskComplexityScorer = null,
        PolicyModelSelector? policyModelSelector = null,
        IToolSelector? toolSelector = null)
    {
        ArgumentNullException.ThrowIfNull(gatekeeper);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(promptAssembler);
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _gatekeeper = gatekeeper;
        _sessions = sessions;
        _strategy = strategy;
        _promptAssembler = promptAssembler;
        _toolRegistry = toolRegistry;
        _config = config.Value;
        _logger = logger;
        _responseEnhancer = responseEnhancer;
        _identityUpdateProjector = identityUpdateProjector;
        _turnEventSink = turnEventSink;
        _contextDiagnosticsService = contextDiagnosticsService;
        _diagnosticsCollector = diagnosticsCollector;
        _gracefulDegradationPolicy = gracefulDegradationPolicy;
        _spendGuardService = spendGuardService;
        _spendTracker = spendTracker;
        _tokenEstimator = tokenEstimator ?? new SimpleTokenEstimator();
        _metrics = metrics;
        _providerHealthTracker = providerHealthTracker;
        _taskComplexityScorer = taskComplexityScorer;
        _policyModelSelector = policyModelSelector;
        _toolSelector = toolSelector ?? NullToolSelector.Instance;
    }

    public async Task<string> ProcessAsync(LeanKernelMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var startedAt = Stopwatch.GetTimestamp();
        var sessionId = !string.IsNullOrWhiteSpace(message.SessionId)
            ? message.SessionId!
            : await _sessions.GetOrCreateSessionIdAsync(message.ChannelId, message.SenderId, ct).ConfigureAwait(false);
        var turnId = ResolveTurnId(message.Metadata);
        var turnScopedMessage = CreateTurnScopedMessage(message, sessionId, turnId);
        TurnToolInvocationTracker? toolInvocationTracker = null;
        IReadOnlyList<ToolDefinition>? visibleToolsForLogging = null;
        using var turnActivity = _diagnosticsCollector?.StartTurnActivity(sessionId, turnId);

        try
        {
            _logger.LogInformation("Processing turn {TurnId} for session {SessionId}", turnId, sessionId);

            await AppendUserTurnAsync(turnScopedMessage, sessionId, ct);

            var budget = ContextBudget.FromConfig(
                _config.LiteLlm.ContextWindowTokens,
                _config.Context);

            var gatedContext = await _gatekeeper.GateContextAsync(turnScopedMessage, budget, sessionId, ct).ConfigureAwait(false);

            var visibleTools = await SelectVisibleToolsAsync(turnScopedMessage, ct);
            visibleToolsForLogging = visibleTools;
            toolInvocationTracker = new TurnToolInvocationTracker();
            visibleTools = WrapToolsForTurn(visibleTools, turnId, sessionId, toolInvocationTracker);

            LogToolAvailability(turnId, sessionId, visibleTools);
            var visibleToolNames = MergeToolNames(gatedContext.ActiveToolNames, visibleTools.Select(tool => tool.Name));

            _logger.LogDebug(
                "Resolved {ToolCount} visible tools for user {UserId}",
                visibleToolNames.Count,
                turnScopedMessage.SenderId);

            var context = CopyWithToolNames(gatedContext, visibleToolNames);

            await StoreContextDiagnosticsAsync(sessionId, turnId, context, budget, ct);

            var systemMessage = _promptAssembler.AssembleSystemMessage(context);

            var strategyContext = new AgentStrategyContext
            {
                SessionId = sessionId,
                TurnId = turnId,
                UserMessage = turnScopedMessage.Content,
                SystemMessage = systemMessage,
                History = context.History,
                Tools = visibleTools.Select(ToolDefinitionAIToolAdapter.Create).ToArray(),
                AvailableToolNames = visibleToolNames,
            };

            var projectedExecution = PredictExecution(strategyContext);
            var estimatedOutputTokens = EstimateOutputTokens(projectedExecution.InputTokens);
            var degradationDecision = _gracefulDegradationPolicy?.Evaluate() ?? new GracefulDegradationDecision();
            var warnings = new List<string>(degradationDecision.Warnings);
            var spendDecision = _spendGuardService?.Evaluate(
                sessionId,
                projectedExecution.Tier,
                projectedExecution.InputTokens,
                estimatedOutputTokens);
            RecordBudgetUtilization(spendDecision);

            if (spendDecision?.Action == SpendGuardAction.Warn && !string.IsNullOrWhiteSpace(spendDecision.Reason))
            {
                warnings.Add(spendDecision.Reason);
            }

            var (response, modelExecutedSuccessfully) = await ResolveModelResponseAsync(
                degradationDecision, spendDecision, strategyContext, sessionId, turnId, ct);

            await RecordDiagnosticsAsync(sessionId, turnId, strategyContext, ct);

            if (strategyContext.QualityGateResult is { Passed: false, FailureReason: { Length: > 0 } failureReason })
            {
                _metrics?.RecordQualityGateFailure(failureReason);
            }

            if (strategyContext.RoutingDecision?.EscalatedFrom is { } escalatedFrom)
            {
                _metrics?.RecordEscalation(escalatedFrom.ToString(), strategyContext.RoutingDecision.SelectedTier.ToString());
            }

            if (modelExecutedSuccessfully && _identityUpdateProjector is not null)
            {
                response = await _identityUpdateProjector.EnhanceAsync(response, context, ct).ConfigureAwait(false);
            }

            EnhancementResult? enhancementResult = null;
            if (modelExecutedSuccessfully && _responseEnhancer is not null)
            {
                try
                {
                    enhancementResult = await _responseEnhancer.EnhanceAsync(
                        new EnhancementStepInput
                        {
                            Response = response,
                            UserMessage = turnScopedMessage.Content,
                            SessionId = sessionId,
                            RetrievedKnowledge = context.RetrievedKnowledge,
                        },
                        ct).ConfigureAwait(false);
                    response = enhancementResult.EnhancedResponse;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Response enhancement failed for session {SessionId} turn {TurnId}; returning original response", sessionId, turnId);
                }
            }

            await RecordResponseEnhancementDiagnosticsAsync(sessionId, turnId, enhancementResult, ct);

            if (modelExecutedSuccessfully && _spendTracker is not null && _spendGuardService is not null)
            {
                var actualTier = strategyContext.RoutingDecision?.SelectedTier ?? projectedExecution.Tier;
                var outputTokens = _tokenEstimator.EstimateTokens(response);
                var actualCostUsd = _spendGuardService.EstimateCostUsd(actualTier, projectedExecution.InputTokens, outputTokens);
                await _spendTracker.RecordSpendAsync(sessionId, turnId, actualCostUsd, ct).ConfigureAwait(false);
                _metrics?.RecordTurnProcessed(strategyContext.ModelUsed ?? _config.LiteLlm.DefaultModel);
                _metrics?.RecordTokensUsed(projectedExecution.InputTokens + outputTokens, strategyContext.ModelUsed ?? _config.LiteLlm.DefaultModel);
            }

            response = AppendWarnings(response, warnings);

            var assistantTimestamp = DateTimeOffset.UtcNow;
            await AppendAssistantTurnAsync(response, sessionId, assistantTimestamp, ct);

            await PublishTurnEventAsync(turnScopedMessage, response, sessionId, turnId, context, strategyContext, assistantTimestamp, ct);

            _logger.LogInformation(
                "Turn {TurnId} completed for session {SessionId}: {ResponseLength} chars",
                turnId,
                sessionId,
                response.Length);

            LogToolExecutionSummary(turnId, sessionId, visibleToolsForLogging, toolInvocationTracker);

            return response;
        }
        catch
        {
            LogToolExecutionSummary(turnId, sessionId, visibleToolsForLogging, toolInvocationTracker);
            throw;
        }
        finally
        {
            _metrics?.RecordTurnLatency(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private async Task AppendUserTurnAsync(LeanKernelMessage turnScopedMessage, string sessionId, CancellationToken ct)
    {
        await _sessions.AppendTurnAsync(
            sessionId,
            new ConversationTurn
            {
                Role = "user",
                Content = turnScopedMessage.Content,
                Timestamp = turnScopedMessage.Timestamp
            },
            ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ToolDefinition>> SelectVisibleToolsAsync(LeanKernelMessage turnScopedMessage, CancellationToken ct)
    {
        var visibleTools = _toolRegistry.GetVisibleTools(new ToolVisibilityContext
        {
            UserId = turnScopedMessage.SenderId
        });

        visibleTools = RemoveRedundantTools(visibleTools);

        var maxTools = _config.LiteLlm.MaxTools;
        if (visibleTools.Count > maxTools)
        {
            _logger.LogWarning(
                "Tool count {Count} exceeds MaxTools ({MaxTools}). Selecting relevant subset.",
                visibleTools.Count,
                maxTools);
            var candidateTools = visibleTools;
            visibleTools = await _toolSelector.SelectToolsAsync(
                turnScopedMessage.Content,
                candidateTools,
                maxTools,
                ct).ConfigureAwait(false);

            visibleTools = EnsureCoreToolsAvailable(visibleTools, candidateTools, maxTools);
        }
        else if (visibleTools.Count >= maxTools * 0.9)
        {
            _logger.LogInformation(
                "Tool count {Count} approaching MaxTools ({MaxTools}) threshold.",
                visibleTools.Count,
                maxTools);
        }

        return visibleTools;
    }

    private IReadOnlyList<ToolDefinition> RemoveRedundantTools(IReadOnlyList<ToolDefinition> tools)
    {
        if (tools.Count == 0)
        {
            return tools;
        }

        var hasBuiltInBrowserTools = BrowserBuiltInToolNames.All(requiredName =>
            tools.Any(t => t.Name.Equals(requiredName, StringComparison.OrdinalIgnoreCase)));
        if (!hasBuiltInBrowserTools)
        {
            return tools;
        }

        var filtered = tools
            .Where(t => !t.Name.StartsWith("web_actions_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (filtered.Count == tools.Count)
        {
            return tools;
        }

        _logger.LogInformation(
            "Removed {RemovedCount} redundant web_actions tools because built-in browser tools are available.",
            tools.Count - filtered.Count);

        return filtered;
    }

    private IReadOnlyList<ToolDefinition> EnsureCoreToolsAvailable(
        IReadOnlyList<ToolDefinition> selectedTools,
        IReadOnlyList<ToolDefinition> visibleTools,
        int maxTools)
    {
        var coreAvailable = visibleTools
            .Where(tool => CoreToolNames.Contains(tool.Name))
            .ToList();
        if (coreAvailable.Count == 0)
        {
            return selectedTools;
        }

        var result = selectedTools.ToList();
        foreach (var coreTool in coreAvailable)
        {
            if (!result.Any(tool => tool.Name.Equals(coreTool.Name, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(coreTool);
            }
        }

        if (result.Count <= maxTools)
        {
            return result;
        }

        for (var i = result.Count - 1; i >= 0 && result.Count > maxTools; i--)
        {
            if (!CoreToolNames.Contains(result[i].Name))
            {
                result.RemoveAt(i);
            }
        }

        return result.Count <= maxTools ? result : result.Take(maxTools).ToList();
    }

    private IReadOnlyList<ToolDefinition> WrapToolsForTurn(
        IReadOnlyList<ToolDefinition> tools,
        string turnId,
        string sessionId,
        TurnToolInvocationTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        if (tools.Count == 0)
        {
            return tools;
        }

        var wrapped = new List<ToolDefinition>(tools.Count);
        foreach (var tool in tools)
        {
            if (tool.Handler is null)
            {
                wrapped.Add(tool);
                continue;
            }

            var originalHandler = tool.Handler;
            var toolName = tool.Name;
            wrapped.Add(tool with
            {
                Handler = async (arguments, cancellationToken) =>
                {
                    var invocationId = tracker.NextInvocationId();
                    var startedAt = Stopwatch.GetTimestamp();
                    _logger.LogInformation(
                        "Tool invocation started (turn={TurnId}, session={SessionId}, invocation={InvocationId}, tool={ToolName}, args={Arguments})",
                        turnId,
                        sessionId,
                        invocationId,
                        toolName,
                        SummarizeArguments(arguments));

                    try
                    {
                        var result = await originalHandler(arguments, cancellationToken).ConfigureAwait(false);
                        var durationMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                        tracker.Record(toolName, result.Success, durationMs);

                        if (result.Success)
                        {
                            _logger.LogInformation(
                                "Tool invocation succeeded (turn={TurnId}, session={SessionId}, invocation={InvocationId}, tool={ToolName}, durationMs={DurationMs:F1}, outputLength={OutputLength})",
                                turnId,
                                sessionId,
                                invocationId,
                                toolName,
                                durationMs,
                                result.Output?.Length ?? 0);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Tool invocation failed (turn={TurnId}, session={SessionId}, invocation={InvocationId}, tool={ToolName}, durationMs={DurationMs:F1}, error={Error})",
                                turnId,
                                sessionId,
                                invocationId,
                                toolName,
                                durationMs,
                                Truncate(result.Error, 320));
                        }

                        return result;
                    }
                    catch (OperationCanceledException)
                    {
                        var durationMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                        tracker.Record(toolName, success: false, durationMs);
                        _logger.LogWarning(
                            "Tool invocation cancelled (turn={TurnId}, session={SessionId}, invocation={InvocationId}, tool={ToolName}, durationMs={DurationMs:F1})",
                            turnId,
                            sessionId,
                            invocationId,
                            toolName,
                            durationMs);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        var durationMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                        tracker.Record(toolName, success: false, durationMs);
                        _logger.LogError(
                            ex,
                            "Tool invocation threw exception (turn={TurnId}, session={SessionId}, invocation={InvocationId}, tool={ToolName}, durationMs={DurationMs:F1})",
                            turnId,
                            sessionId,
                            invocationId,
                            toolName,
                            durationMs);
                        throw;
                    }
                }
            });
        }

        return wrapped;
    }

    private void LogToolAvailability(string turnId, string sessionId, IReadOnlyList<ToolDefinition> tools)
    {
        var msTodoTools = tools
            .Where(tool => tool.Name.StartsWith("ms-todo", StringComparison.OrdinalIgnoreCase))
            .Select(tool => tool.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.LogInformation(
            "Tool availability (turn={TurnId}, session={SessionId}, total={ToolCount}, msTodoCount={MsTodoCount}, sample={ToolSample})",
            turnId,
            sessionId,
            tools.Count,
            msTodoTools.Length,
            SummarizeToolNames(tools));

        if (msTodoTools.Length > 0)
        {
            _logger.LogInformation(
                "MS To Do tools available for turn {TurnId}: {MsTodoTools}",
                turnId,
                string.Join(", ", msTodoTools));
        }
    }

    private void LogToolExecutionSummary(
        string turnId,
        string sessionId,
        IReadOnlyList<ToolDefinition>? visibleTools,
        TurnToolInvocationTracker? tracker)
    {
        if (visibleTools is null)
        {
            return;
        }

        if (tracker is null)
        {
            _logger.LogInformation(
                "Tool execution summary unavailable (turn={TurnId}, session={SessionId}, offered={OfferedCount})",
                turnId,
                sessionId,
                visibleTools.Count);
            return;
        }

        var totalInvocations = tracker.TotalInvocations;
        var successfulInvocations = tracker.SuccessfulInvocations;
        var failedInvocations = tracker.FailedInvocations;

        _logger.LogInformation(
            "Tool execution summary (turn={TurnId}, session={SessionId}, offered={OfferedCount}, invoked={InvokedCount}, succeeded={SucceededCount}, failed={FailedCount}, tools={ExecutedTools})",
            turnId,
            sessionId,
            visibleTools.Count,
            totalInvocations,
            successfulInvocations,
            failedInvocations,
            tracker.GetSummary());

        if (totalInvocations == 0)
        {
            _logger.LogInformation(
                "No tool invocations were executed for turn {TurnId}; any tool-usage claims in assistant text were not backed by runtime calls.",
                turnId);
        }
    }

    private static string SummarizeToolNames(IReadOnlyList<ToolDefinition> tools, int maxNames = 20)
    {
        if (tools.Count == 0)
        {
            return "(none)";
        }

        var names = tools
            .Select(tool => tool.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(maxNames)
            .ToArray();

        var suffix = tools.Count > maxNames ? $", +{tools.Count - maxNames} more" : string.Empty;
        return string.Join(", ", names) + suffix;
    }

    private static string SummarizeArguments(IDictionary<string, object?> arguments)
    {
        if (arguments.Count == 0)
        {
            return "{}";
        }

        var ordered = arguments.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();
        foreach (var (key, value) in ordered)
        {
            var renderedValue = IsSensitiveKey(key)
                ? "[redacted]"
                : Truncate(value?.ToString(), 160);
            parts.Add($"{key}={renderedValue}");
        }

        return "{" + string.Join(", ", parts) + "}";
    }

    private static bool IsSensitiveKey(string key)
        => key.Contains("token", StringComparison.OrdinalIgnoreCase)
            || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("password", StringComparison.OrdinalIgnoreCase)
            || key.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || key.Contains("api_key", StringComparison.OrdinalIgnoreCase)
            || key.Contains("apikey", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }

    private async Task StoreContextDiagnosticsAsync(string sessionId, string turnId, ConversationContext context, ContextBudget budget, CancellationToken ct)
    {
        if (_contextDiagnosticsService is null)
        {
            return;
        }

        try
        {
            await _contextDiagnosticsService.StoreContextDiagnosticsAsync(
                    sessionId,
                    turnId,
                    CreateContextSnapshot(context, budget, sessionId, turnId),
                    ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Context diagnostics persistence failed for session {SessionId} turn {TurnId}", sessionId, turnId);
        }
    }

    private async Task<(string Response, bool ModelExecutedSuccessfully)> ResolveModelResponseAsync(
        GracefulDegradationDecision degradationDecision,
        SpendGuardDecision? spendDecision,
        AgentStrategyContext strategyContext,
        string sessionId,
        string turnId,
        CancellationToken ct)
    {
        if (!degradationDecision.AllowModelExecution)
        {
            return (degradationDecision.UserMessage
                ?? "LeanKernel cannot reach the configured model provider right now. Please try again shortly.", false);
        }

        if (spendDecision?.Action == SpendGuardAction.Block)
        {
            return (spendDecision.Reason, false);
        }

        try
        {
            var response = await _strategy.InvokeAsync(strategyContext, ct).ConfigureAwait(false);
            _providerHealthTracker?.RecordProbeResult(ProviderNames.LiteLlm, ProviderProbeResult.Healthy("Model invocation succeeded."));
            return (response, true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _providerHealthTracker?.RecordProbeResult(ProviderNames.LiteLlm, ProviderProbeResult.Unhealthy("Model invocation failed.", ex.Message));
            _logger.LogWarning(ex, "Model invocation failed for session {SessionId} turn {TurnId}; returning degraded response", sessionId, turnId);
            return ("LeanKernel cannot reach the configured model provider right now. Please try again shortly.", false);
        }
    }

    private async Task RecordDiagnosticsAsync(string sessionId, string turnId, AgentStrategyContext strategyContext, CancellationToken ct)
    {
        if (_diagnosticsCollector is null)
        {
            return;
        }

        if (strategyContext.OrchestrationResult is not null)
        {
            await TryRecordDiagnosticAsync(
                () => _diagnosticsCollector.RecordOrchestrationAsync(sessionId, turnId, strategyContext.OrchestrationResult, ct),
                "Orchestration", sessionId, turnId, ct);
        }

        if (strategyContext.RoutingDecision is not null)
        {
            await TryRecordDiagnosticAsync(
                () => _diagnosticsCollector.RecordModelRoutingAsync(sessionId, turnId, strategyContext.RoutingDecision, ct),
                "Routing", sessionId, turnId, ct);
        }

        if (strategyContext.QualityGateResult is not null)
        {
            await TryRecordDiagnosticAsync(
                () => _diagnosticsCollector.RecordQualityGateAsync(sessionId, turnId, strategyContext.QualityGateResult, ct),
                "Quality gate", sessionId, turnId, ct);
        }
    }

    private async Task TryRecordDiagnosticAsync(Func<Task> recordAsync, string label, string sessionId, string turnId, CancellationToken ct)
    {
        try
        {
            await recordAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Label} diagnostics persistence failed for session {SessionId} turn {TurnId}", label, sessionId, turnId);
        }
    }

    private async Task RecordResponseEnhancementDiagnosticsAsync(string sessionId, string turnId, EnhancementResult? enhancementResult, CancellationToken ct)
    {
        if (_diagnosticsCollector is null || enhancementResult is null)
        {
            return;
        }

        try
        {
            await _diagnosticsCollector.RecordResponseEnhancementAsync(
                sessionId, turnId, enhancementResult, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Response enhancement diagnostics persistence failed for session {SessionId} turn {TurnId}", sessionId, turnId);
        }
    }

    private async Task AppendAssistantTurnAsync(string response, string sessionId, DateTimeOffset assistantTimestamp, CancellationToken ct)
    {
        await _sessions.AppendTurnAsync(
            sessionId,
            new ConversationTurn
            {
                Role = "assistant",
                Content = response,
                Timestamp = assistantTimestamp
            },
            ct).ConfigureAwait(false);
    }

    private async Task PublishTurnEventAsync(
        LeanKernelMessage turnScopedMessage,
        string response,
        string sessionId,
        string turnId,
        ConversationContext context,
        AgentStrategyContext strategyContext,
        DateTimeOffset assistantTimestamp,
        CancellationToken ct)
    {
        if (_turnEventSink is null)
        {
            return;
        }

        try
        {
            await _turnEventSink.PublishAsync(
                new TurnEvent
                {
                    SessionId = sessionId,
                    TurnId = turnId,
                    Role = "assistant",
                    Content = response,
                    UserMessage = turnScopedMessage.Content,
                    AssistantResponse = response,
                    Timestamp = assistantTimestamp,
                    Context = context,
                    ModelUsed = strategyContext.ModelUsed ?? _config.LiteLlm.DefaultModel,
                    RoutingDecision = strategyContext.RoutingDecision,
                    OrchestrationResult = strategyContext.OrchestrationResult,
                },
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Turn event publication failed for session {SessionId} turn {TurnId}", sessionId, turnId);
        }
    }

    private void RecordBudgetUtilization(SpendGuardDecision? spendDecision)
    {
        if (spendDecision is null)
        {
            return;
        }

        var ratios = new List<double>();

        if (spendDecision.DailyLimitUsd > 0)
        {
            ratios.Add((double)(spendDecision.DailySpendUsd / spendDecision.DailyLimitUsd));
        }

        if (spendDecision.SessionLimitUsd > 0)
        {
            ratios.Add((double)(spendDecision.SessionSpendUsd / spendDecision.SessionLimitUsd));
        }

        if (spendDecision.MonthlyLimitUsd > 0)
        {
            ratios.Add((double)(spendDecision.MonthlySpendUsd / spendDecision.MonthlyLimitUsd));
        }

        if (ratios.Count > 0)
        {
            _metrics?.RecordBudgetUtilization(ratios.Max());
        }
    }

    private ProjectedExecution PredictExecution(AgentStrategyContext strategyContext)
    {
        ArgumentNullException.ThrowIfNull(strategyContext);

        if (_config.Routing.Enabled && _taskComplexityScorer is not null && _policyModelSelector is not null)
        {
            var assessment = _taskComplexityScorer.Score(strategyContext);
            var decision = _policyModelSelector.Select(assessment);
            return new ProjectedExecution(
                decision.SelectedTier,
                assessment.MessageTokens + assessment.HistoryTokens + assessment.SystemTokens);
        }

        var inputTokens = _tokenEstimator.EstimateTokens(strategyContext.UserMessage)
            + _tokenEstimator.EstimateTokens(strategyContext.SystemMessage)
            + strategyContext.History.Sum(turn => _tokenEstimator.EstimateTokens(turn.Content));

        return new ProjectedExecution(ResolveDefaultModelTier(), inputTokens);
    }

    private int EstimateOutputTokens(int inputTokens)
        => Math.Clamp(Math.Max(128, inputTokens / 2), 128, 2048);

    private ModelTier ResolveDefaultModelTier()
    {
        if (string.Equals(_config.LiteLlm.DefaultModel, _config.Routing.Economy.Model, StringComparison.OrdinalIgnoreCase))
        {
            return ModelTier.Economy;
        }

        if (string.Equals(_config.LiteLlm.DefaultModel, _config.Routing.Premium.Model, StringComparison.OrdinalIgnoreCase))
        {
            return ModelTier.Premium;
        }

        return ModelTier.Standard;
    }

    private static string AppendWarnings(string response, IReadOnlyCollection<string> warnings)
    {
        var distinctWarnings = warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctWarnings.Length == 0)
        {
            return response;
        }

        var notice = string.Join(Environment.NewLine, distinctWarnings.Select(warning => $"- {warning}"));
        return string.IsNullOrWhiteSpace(response)
            ? $"System notices:{Environment.NewLine}{notice}"
            : $"{response}{Environment.NewLine}{Environment.NewLine}System notices:{Environment.NewLine}{notice}";
    }

    private ContextDiagnosticsSnapshot CreateContextSnapshot(
        ConversationContext context,
        ContextBudget budget,
        string sessionId,
        string turnId)
        => new()
        {
            Admissions = context.AdmissionLog,
            BudgetUsage = context.BudgetUsage ?? new ContextBudgetUsage(),
            Budget = budget,
            TotalBudgetTokens = _config.LiteLlm.ContextWindowTokens,
            ResponseHeadroomRatio = _config.Context.ResponseHeadroomRatio,
            HistoryDiagnostics = context.HistoryDiagnostics,
            RetrievalDiagnostics = context.RetrievalDiagnostics is null
                ? null
                : context.RetrievalDiagnostics with
                {
                    SessionId = sessionId,
                    TurnId = turnId,
                },
            Timestamp = DateTimeOffset.UtcNow,
        };

    private static LeanKernelMessage CreateTurnScopedMessage(
        LeanKernelMessage message,
        string sessionId,
        string turnId)
        => message with
        {
            SessionId = sessionId,
            Metadata = CopyMetadata(message.Metadata, turnId),
        };

    private static IReadOnlyDictionary<string, string> CopyMetadata(
        IReadOnlyDictionary<string, string>? metadata,
        string turnId)
    {
        var values = metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);

        values["turn_id"] = turnId;
        values["turnId"] = turnId;
        return values;
    }

    private static string ResolveTurnId(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is not null)
        {
            if (metadata.TryGetValue("turn_id", out var turnId) && !string.IsNullOrWhiteSpace(turnId))
            {
                return turnId.Trim();
            }

            if (metadata.TryGetValue("turnId", out turnId) && !string.IsNullOrWhiteSpace(turnId))
            {
                return turnId.Trim();
            }
        }

        return Guid.NewGuid().ToString();
    }

    private static IReadOnlyList<string> MergeToolNames(
        IReadOnlyList<string> existingToolNames,
        IEnumerable<string> visibleToolNames)
    {
        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in existingToolNames)
        {
            if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
            {
                merged.Add(name);
            }
        }

        foreach (var name in visibleToolNames)
        {
            if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
            {
                merged.Add(name);
            }
        }

        return merged;
    }

    private static ConversationContext CopyWithToolNames(
        ConversationContext context,
        IReadOnlyList<string> toolNames)
        => new()
        {
            SystemPrompt = context.SystemPrompt,
            SessionId = context.SessionId,
            History = context.History,
            WikiFacts = context.WikiFacts,
            RetrievedKnowledge = context.RetrievedKnowledge,
            Identity = context.Identity,
            Onboarding = context.Onboarding,
            ActiveToolNames = toolNames,
            BudgetUsage = context.BudgetUsage,
            AdmissionLog = context.AdmissionLog,
            HistoryDiagnostics = context.HistoryDiagnostics,
            RetrievalDiagnostics = context.RetrievalDiagnostics,
        };

    private sealed class TurnToolInvocationTracker
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, ToolInvocationStats> _stats = new(StringComparer.OrdinalIgnoreCase);
        private int _nextInvocationId;

        public int TotalInvocations { get; private set; }

        public int SuccessfulInvocations { get; private set; }

        public int FailedInvocations => TotalInvocations - SuccessfulInvocations;

        public int NextInvocationId()
            => Interlocked.Increment(ref _nextInvocationId);

        public void Record(string toolName, bool success, double durationMs)
        {
            lock (_gate)
            {
                TotalInvocations++;
                if (success)
                {
                    SuccessfulInvocations++;
                }

                if (!_stats.TryGetValue(toolName, out var stats))
                {
                    stats = new ToolInvocationStats();
                    _stats[toolName] = stats;
                }

                stats.Count++;
                stats.TotalDurationMs += durationMs;
                if (success)
                {
                    stats.SuccessCount++;
                }
            }
        }

        public string GetSummary()
        {
            lock (_gate)
            {
                if (_stats.Count == 0)
                {
                    return "(none)";
                }

                var builder = new StringBuilder();
                foreach (var (toolName, stats) in _stats.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (builder.Length > 0)
                    {
                        builder.Append("; ");
                    }

                    var averageDurationMs = stats.Count == 0 ? 0d : stats.TotalDurationMs / stats.Count;
                    builder.Append(toolName)
                        .Append(':')
                        .Append(stats.SuccessCount)
                        .Append('/')
                        .Append(stats.Count)
                        .Append(" ok, avg=")
                        .Append(averageDurationMs.ToString("F1"))
                        .Append("ms");
                }

                return builder.ToString();
            }
        }

        private sealed class ToolInvocationStats
        {
            public int Count { get; set; }

            public int SuccessCount { get; set; }

            public double TotalDurationMs { get; set; }
        }
    }

    private readonly record struct ProjectedExecution(ModelTier Tier, int InputTokens);
}
