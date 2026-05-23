using System.Text.RegularExpressions;
using LeanKernel.Agents.Routing;
using LeanKernel.Agents.Strategies;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Agents.Orchestration;

/// <summary>
/// Determines whether a request should use coordinator-worker orchestration.
/// </summary>
public sealed class OrchestrationDecider(
    TaskComplexityScorer complexityScorer,
    ILogger<OrchestrationDecider> logger)
{
    private const double ComplexityThreshold = 0.55;

    private static readonly string[] MultiStepMarkers =
    [
        "first",
        "second",
        "third",
        "then",
        "next",
        "after",
        "finally",
        "step",
        "steps",
        "and also",
        "as well as",
        "while also"
    ];

    private static readonly string[] DelegationMarkers =
    [
        "delegate",
        "delegation",
        "worker",
        "workers",
        "specialist",
        "parallel",
        "parallelize",
        "coordinate",
        "orchestrate",
        "split this up"
    ];

    private static readonly Regex OrderedListPattern = new(@"(^|\n)\s*(\d+[\).]|[-*])\s+", RegexOptions.Compiled);

    private readonly TaskComplexityScorer _complexityScorer = complexityScorer ?? throw new ArgumentNullException(nameof(complexityScorer));
    private readonly ILogger<OrchestrationDecider> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Decides whether the supplied strategy context should use orchestration.
    /// </summary>
    /// <param name="context">The current strategy context.</param>
    /// <returns>The orchestration decision.</returns>
    public OrchestrationDecision Decide(AgentStrategyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (ContainsExplicitDelegation(context.UserMessage))
        {
            return CreateDecision(true, "explicit delegation request detected");
        }

        if (ContainsMultiStepInstructions(context.UserMessage))
        {
            return CreateDecision(true, "multi-step task indicators detected");
        }

        var assessment = _complexityScorer.Score(context);
        if (assessment.Score >= ComplexityThreshold)
        {
            return CreateDecision(true, $"complexity score {assessment.Score:0.00} exceeded orchestration threshold");
        }

        return CreateDecision(false, $"complexity score {assessment.Score:0.00} below orchestration threshold");
    }

    private OrchestrationDecision CreateDecision(bool shouldOrchestrate, string reason)
    {
        var decision = new OrchestrationDecision
        {
            ShouldOrchestrate = shouldOrchestrate,
            Reason = reason
        };

        _logger.LogDebug(
            "Orchestration decision: enabled={Enabled}, reason={Reason}",
            shouldOrchestrate,
            reason);

        return decision;
    }

    private static bool ContainsExplicitDelegation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lowered = text.ToLowerInvariant();
        return DelegationMarkers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal));
    }

    private static bool ContainsMultiStepInstructions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (OrderedListPattern.IsMatch(text))
        {
            return true;
        }

        var lowered = text.ToLowerInvariant();
        return MultiStepMarkers.Count(marker => lowered.Contains(marker, StringComparison.Ordinal)) >= 2;
    }
}
