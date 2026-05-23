using System.Diagnostics;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Orchestration;
using LeanKernel.Agents.Routing;
using LeanKernel.Agents.Strategies;
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
        PolicyModelSelector? policyModelSelector = null)
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
        using var turnActivity = _diagnosticsCollector?.StartTurnActivity(sessionId, turnId);

        try
        {
            _logger.LogInformation("Processing turn {TurnId} for session {SessionId}", turnId, sessionId);

            await _sessions.AppendTurnAsync(
                sessionId,
                new ConversationTurn
                {
                    Role = "user",
                    Content = turnScopedMessage.Content,
                    Timestamp = turnScopedMessage.Timestamp
                },
                ct).ConfigureAwait(false);

            var budget = ContextBudget.FromConfig(
                _config.LiteLlm.ContextWindowTokens,
                _config.Context);

            var gatedContext = await _gatekeeper.GateContextAsync(turnScopedMessage, budget, sessionId, ct).ConfigureAwait(false);

            var visibleTools = _toolRegistry.GetVisibleTools(new ToolVisibilityContext
            {
                UserId = turnScopedMessage.SenderId
            });
            var visibleToolNames = MergeToolNames(gatedContext.ActiveToolNames, visibleTools.Select(tool => tool.Name));

            _logger.LogDebug(
                "Resolved {ToolCount} visible tools for user {UserId}",
                visibleToolNames.Count,
                turnScopedMessage.SenderId);

            var context = CopyWithToolNames(gatedContext, visibleToolNames);

            if (_contextDiagnosticsService is not null)
            {
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

            string response;
            var modelExecutedSuccessfully = false;

            if (!degradationDecision.AllowModelExecution)
            {
                response = degradationDecision.UserMessage
                    ?? "LeanKernel cannot reach the configured model provider right now. Please try again shortly.";
            }
            else if (spendDecision?.Action == SpendGuardAction.Block)
            {
                response = spendDecision.Reason;
            }
            else
            {
                try
                {
                    response = await _strategy.InvokeAsync(strategyContext, ct).ConfigureAwait(false);
                    modelExecutedSuccessfully = true;
                    _providerHealthTracker?.RecordProbeResult(ProviderNames.LiteLlm, ProviderProbeResult.Healthy("Model invocation succeeded."));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _providerHealthTracker?.RecordProbeResult(ProviderNames.LiteLlm, ProviderProbeResult.Unhealthy("Model invocation failed.", ex.Message));
                    _logger.LogWarning(ex, "Model invocation failed for session {SessionId} turn {TurnId}; returning degraded response", sessionId, turnId);
                    response = "LeanKernel cannot reach the configured model provider right now. Please try again shortly.";
                }
            }

            if (_diagnosticsCollector is not null && strategyContext.OrchestrationResult is not null)
            {
                try
                {
                    await _diagnosticsCollector.RecordOrchestrationAsync(
                        sessionId,
                        turnId,
                        strategyContext.OrchestrationResult,
                        ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Orchestration diagnostics persistence failed for session {SessionId} turn {TurnId}", sessionId, turnId);
                }
            }

            if (_diagnosticsCollector is not null && strategyContext.RoutingDecision is not null)
            {
                try
                {
                    await _diagnosticsCollector.RecordModelRoutingAsync(
                        sessionId,
                        turnId,
                        strategyContext.RoutingDecision,
                        ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Routing diagnostics persistence failed for session {SessionId} turn {TurnId}", sessionId, turnId);
                }
            }

            if (_diagnosticsCollector is not null && strategyContext.QualityGateResult is not null)
            {
                try
                {
                    await _diagnosticsCollector.RecordQualityGateAsync(
                        sessionId,
                        turnId,
                        strategyContext.QualityGateResult,
                        ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Quality gate diagnostics persistence failed for session {SessionId} turn {TurnId}", sessionId, turnId);
                }
            }

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

            if (_diagnosticsCollector is not null && enhancementResult is not null)
            {
                try
                {
                    await _diagnosticsCollector.RecordResponseEnhancementAsync(
                        sessionId,
                        turnId,
                        enhancementResult,
                        ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Response enhancement diagnostics persistence failed for session {SessionId} turn {TurnId}", sessionId, turnId);
                }
            }

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
            await _sessions.AppendTurnAsync(
                sessionId,
                new ConversationTurn
                {
                    Role = "assistant",
                    Content = response,
                    Timestamp = assistantTimestamp
                },
                ct).ConfigureAwait(false);

            if (_turnEventSink is not null)
            {
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

            _logger.LogInformation(
                "Turn {TurnId} completed for session {SessionId}: {ResponseLength} chars",
                turnId,
                sessionId,
                response.Length);

            return response;
        }
        finally
        {
            _metrics?.RecordTurnLatency(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
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

    private readonly record struct ProjectedExecution(ModelTier Tier, int InputTokens);
}
