using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents.Routing;

/// <summary>
/// Determines when a routing decision can escalate to a higher tier.
/// </summary>
public sealed class EscalationPolicy(
    PolicyModelSelector selector,
    IOptions<LeanKernelConfig> config,
    ILogger<EscalationPolicy> logger)
{
    private readonly PolicyModelSelector _selector = selector ?? throw new ArgumentNullException(nameof(selector));
    private readonly RoutingConfig _routing = config?.Value.Routing ?? throw new ArgumentNullException(nameof(config));
    private readonly ILogger<EscalationPolicy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Attempts to escalate the supplied routing decision.
    /// </summary>
    /// <param name="currentDecision">The current routing decision.</param>
    /// <param name="assessment">The originating complexity assessment.</param>
    /// <param name="qualityOutcome">The quality gate outcome that triggered the escalation attempt.</param>
    /// <returns>The next routing decision, or <see langword="null"/> when escalation is not allowed.</returns>
    public RoutingDecision? TryEscalate(
        RoutingDecision currentDecision,
        TaskComplexityAssessment assessment,
        QualityOutcome qualityOutcome)
    {
        ArgumentNullException.ThrowIfNull(currentDecision);
        ArgumentNullException.ThrowIfNull(assessment);

        if (currentDecision.EscalationAttempt >= _routing.MaxEscalationAttempts)
        {
            _logger.LogDebug(
                "Escalation blocked for model {Model}: attempt {Attempt} reached max {MaxAttempts}",
                currentDecision.SelectedModel,
                currentDecision.EscalationAttempt,
                _routing.MaxEscalationAttempts);
            return null;
        }

        ModelTier? nextTier = currentDecision.SelectedTier switch
        {
            ModelTier.Economy => ModelTier.Standard,
            ModelTier.Standard => ModelTier.Premium,
            ModelTier.Premium => null,
            _ => null
        };

        if (nextTier is null)
        {
            _logger.LogDebug(
                "Escalation blocked for model {Model}: no higher tier exists",
                currentDecision.SelectedModel);
            return null;
        }

        var escalatedFactors = currentDecision.Factors
            .Concat(["quality-outcome:" + qualityOutcome, "escalated-from:" + currentDecision.SelectedTier])
            .ToArray();

        return _selector.CreateDecisionForTier(
            nextTier.Value,
            assessment,
            $"Escalated from {currentDecision.SelectedTier} after {qualityOutcome}.",
            currentDecision.SelectedTier,
            currentDecision.EscalationAttempt + 1,
            escalatedFactors);
    }
}
