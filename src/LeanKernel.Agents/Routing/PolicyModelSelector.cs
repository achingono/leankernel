using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents.Routing;

/// <summary>
/// Selects a model tier and model name from a complexity assessment.
/// </summary>
public sealed class PolicyModelSelector(
    IOptions<LeanKernelConfig> config,
    ILogger<PolicyModelSelector> logger)
{
    private readonly RoutingConfig _routing = config?.Value.Routing ?? throw new ArgumentNullException(nameof(config));
    private readonly ILogger<PolicyModelSelector> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Selects the configured model for the supplied assessment.
    /// </summary>
    /// <param name="assessment">The complexity assessment.</param>
    /// <returns>The routing decision for the current turn.</returns>
    public RoutingDecision Select(TaskComplexityAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        var tier = assessment.Score switch
        {
            < 0.3 => ModelTier.Economy,
            <= 0.7 => ModelTier.Standard,
            _ => ModelTier.Premium
        };

        return CreateDecisionForTier(
            tier,
            assessment,
            $"{tier} tier selected for complexity score {assessment.Score:0.00} using configured cost-weighted policy.");
    }

    /// <summary>
    /// Creates a routing decision for a specific tier.
    /// </summary>
    /// <param name="tier">The selected model tier.</param>
    /// <param name="assessment">The originating complexity assessment.</param>
    /// <param name="reason">The routing reason.</param>
    /// <param name="escalatedFrom">The previous tier, if the decision is an escalation.</param>
    /// <param name="escalationAttempt">The escalation attempt number.</param>
    /// <param name="factors">Optional replacement factors.</param>
    /// <returns>The created routing decision.</returns>
    public RoutingDecision CreateDecisionForTier(
        ModelTier tier,
        TaskComplexityAssessment assessment,
        string reason,
        ModelTier? escalatedFrom = null,
        int escalationAttempt = 0,
        IReadOnlyList<string>? factors = null)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var modelConfig = GetTierConfig(tier);
        var decision = new RoutingDecision
        {
            SelectedTier = tier,
            SelectedModel = modelConfig.Model,
            ComplexityScore = assessment.Score,
            Reason = reason,
            Factors = factors ?? assessment.Factors,
            EscalatedFrom = escalatedFrom,
            EscalationAttempt = escalationAttempt,
        };

        _logger.LogDebug(
            "Policy selected model {Model} on tier {Tier} for score {Score}",
            decision.SelectedModel,
            decision.SelectedTier,
            decision.ComplexityScore);

        return decision;
    }

    private ModelTierConfig GetTierConfig(ModelTier tier)
        => tier switch
        {
            ModelTier.Economy => _routing.Economy,
            ModelTier.Standard => _routing.Standard,
            ModelTier.Premium => _routing.Premium,
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unsupported model tier.")
        };
}
