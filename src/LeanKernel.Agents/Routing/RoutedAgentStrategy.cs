using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Strategies;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents.Routing;

/// <summary>
/// Routes agent turns to the most appropriate configured model tier.
/// </summary>
public sealed class RoutedAgentStrategy(
    AgentFactory agentFactory,
    TaskComplexityScorer complexityScorer,
    PolicyModelSelector modelSelector,
    EscalationPolicy escalationPolicy,
    IResponseQualityGate qualityGate,
    IOptions<LeanKernelConfig> config,
    ILogger<RoutedAgentStrategy> logger) : IAgentStrategy
{
    private readonly AgentFactory _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
    private readonly TaskComplexityScorer _complexityScorer = complexityScorer ?? throw new ArgumentNullException(nameof(complexityScorer));
    private readonly PolicyModelSelector _modelSelector = modelSelector ?? throw new ArgumentNullException(nameof(modelSelector));
    private readonly EscalationPolicy _escalationPolicy = escalationPolicy ?? throw new ArgumentNullException(nameof(escalationPolicy));
    private readonly IResponseQualityGate _qualityGate = qualityGate ?? throw new ArgumentNullException(nameof(qualityGate));
    private readonly RoutingConfig _routing = config?.Value.Routing ?? throw new ArgumentNullException(nameof(config));
    private readonly ILogger<RoutedAgentStrategy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Gets the strategy name.
    /// </summary>
    public string Name => "routed";

    /// <summary>
    /// Invokes the routed strategy.
    /// </summary>
    /// <param name="context">The strategy context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The final assistant response text.</returns>
    public async Task<string> InvokeAsync(AgentStrategyContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var assessment = _complexityScorer.Score(context);
        var decision = _modelSelector.Select(assessment);

        while (true)
        {
            var response = await InvokeModelAsync(context, decision, ct).ConfigureAwait(false);
            var qualityResult = EvaluateQuality(context, response);
            context.QualityGateResult = qualityResult;

            LogDecision(decision, qualityResult, context);

            if (qualityResult.Passed)
            {
                ApplyFinalOutcome(context, decision, qualityResult);
                return response;
            }

            var escalatedDecision = _escalationPolicy.TryEscalate(decision, assessment, qualityResult.Outcome);
            if (escalatedDecision is null)
            {
                ApplyFinalOutcome(context, decision, qualityResult);
                return response;
            }

            _logger.LogWarning(
                "Escalating session {SessionId} turn {TurnId} from model {Model} tier {Tier} after quality outcome {Outcome}: {FailureReason}",
                context.SessionId,
                context.TurnId,
                decision.SelectedModel,
                decision.SelectedTier,
                qualityResult.Outcome,
                qualityResult.FailureReason);

            decision = escalatedDecision;
        }
    }

    private async Task<string> InvokeModelAsync(AgentStrategyContext context, RoutingDecision decision, CancellationToken ct)
    {
        var chatClient = _agentFactory.GetChatClientForModel(decision.SelectedModel);
        var messages = AgentInvocationBuilder.BuildMessages(context);
        var options = AgentInvocationBuilder.BuildOptions(context);

        _logger.LogInformation(
            "Routing turn {TurnId} for session {SessionId} to model {Model} on tier {Tier} with score {Score:0.00} (attempt {Attempt})",
            context.TurnId,
            context.SessionId,
            decision.SelectedModel,
            decision.SelectedTier,
            decision.ComplexityScore,
            decision.EscalationAttempt);

        var response = await chatClient.GetResponseAsync(messages, options, ct).ConfigureAwait(false);
        context.TokensUsed = ChatResponseMetadataReader.GetTokensUsed(response);
        return response.Text ?? string.Empty;
    }

    private QualityGateResult EvaluateQuality(AgentStrategyContext context, string response)
        => _qualityGate.Evaluate(new QualityEvaluationContext
        {
            UserMessage = context.UserMessage,
            Response = response,
            MinOutputLength = _routing.QualityMinOutputLength,
            MinConstraintCoverage = _routing.QualityMinConstraintCoverage,
        });

    private void ApplyFinalOutcome(
        AgentStrategyContext context,
        RoutingDecision decision,
        QualityGateResult qualityResult)
    {
        context.ModelUsed = decision.SelectedModel;
        context.RoutingDecision = decision;
        context.QualityOutcome = qualityResult.Outcome;
        context.QualityGateResult = qualityResult;
    }

    private void LogDecision(
        RoutingDecision decision,
        QualityGateResult qualityResult,
        AgentStrategyContext context)
    {
        var checkSummary = string.Join(
            ",",
            qualityResult.Checks.Select(check => $"{check.CheckName}:{(check.Passed ? "pass" : "fail")}:{check.Score:0.00}"));

        _logger.LogInformation(
            "Routing decision completed for session {SessionId} turn {TurnId}: model={Model}, tier={Tier}, score={Score:0.00}, attempt={Attempt}, outcome={Outcome}, passed={Passed}, qualityScore={QualityScore:0.00}, checks={Checks}, factors={Factors}, reason={Reason}, failureReason={FailureReason}",
            context.SessionId,
            context.TurnId,
            decision.SelectedModel,
            decision.SelectedTier,
            decision.ComplexityScore,
            decision.EscalationAttempt,
            qualityResult.Outcome,
            qualityResult.Passed,
            qualityResult.OverallScore,
            checkSummary,
            string.Join(",", decision.Factors),
            decision.Reason,
            qualityResult.FailureReason);
    }
}
