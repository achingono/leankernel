using System.Text.RegularExpressions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Agents.Strategies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents.Routing;

/// <summary>
/// Scores task complexity deterministically from prompt and context inputs.
/// </summary>
public sealed class TaskComplexityScorer(
    ITokenEstimator tokenEstimator,
    IOptions<LeanKernelConfig> config,
    ILogger<TaskComplexityScorer> logger)
{
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
        "steps"
    ];

    private static readonly Regex OrderedListPattern = new(@"(^|\n)\s*(\d+[\).]|[-*])\s+", RegexOptions.Compiled);

    private readonly ITokenEstimator _tokenEstimator = tokenEstimator ?? throw new ArgumentNullException(nameof(tokenEstimator));
    private readonly ComplexityScoringConfig _scoring = config?.Value.Routing.Scoring ?? throw new ArgumentNullException(nameof(config));
    private readonly ILogger<TaskComplexityScorer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Scores the supplied strategy context.
    /// </summary>
    /// <param name="context">The strategy context to score.</param>
    /// <returns>The complexity assessment for the current turn.</returns>
    public TaskComplexityAssessment Score(AgentStrategyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messageTokens = _tokenEstimator.EstimateTokens(context.UserMessage);
        var systemTokens = _tokenEstimator.EstimateTokens(context.SystemMessage);
        var historyTokens = context.History.Sum(turn => _tokenEstimator.EstimateTokens(turn.Content));
        var toolCount = context.Tools?.Count ?? context.AvailableToolNames.Count;
        var factors = new List<string>();
        var score = ScoreMessageLength(messageTokens, factors);

        if (toolCount > 0)
        {
            var toolBoost = _scoring.ToolUsageComplexityBoost * Math.Min(1.0, toolCount / 3.0);
            score += toolBoost;
            factors.Add($"tooling:{toolCount}");
        }

        if (context.History.Count > 1)
        {
            var multiTurnBoost = _scoring.MultiTurnComplexityBoost * Math.Min(1.0, context.History.Count / 6.0);
            score += multiTurnBoost;
            factors.Add($"history-turns:{context.History.Count}");
        }

        var longContextFactors = new List<string>();
        var longContextBoost = 0.0;

        if (historyTokens >= _scoring.MediumComplexityTokenThreshold)
        {
            longContextBoost += _scoring.LongContextComplexityBoost / 2.0;
            longContextFactors.Add($"history-tokens:{historyTokens}");
        }

        if (systemTokens >= _scoring.MediumComplexityTokenThreshold)
        {
            longContextBoost += _scoring.LongContextComplexityBoost / 2.0;
            longContextFactors.Add($"system-tokens:{systemTokens}");
        }

        if (longContextBoost > 0)
        {
            score += longContextBoost;
            factors.AddRange(longContextFactors);
        }

        if (ContainsMultiStepInstructions(context.UserMessage) || ContainsMultiStepInstructions(context.SystemMessage))
        {
            score += 0.15;
            factors.Add("multi-step-instructions");
        }

        var normalizedScore = Math.Round(Math.Min(1.0, score), 4, MidpointRounding.AwayFromZero);

        _logger.LogDebug(
            "Task complexity scored: score={Score}, messageTokens={MessageTokens}, historyTokens={HistoryTokens}, systemTokens={SystemTokens}, toolCount={ToolCount}",
            normalizedScore,
            messageTokens,
            historyTokens,
            systemTokens,
            toolCount);

        return new TaskComplexityAssessment
        {
            Score = normalizedScore,
            Factors = factors,
            MessageTokens = messageTokens,
            HistoryTokens = historyTokens,
            SystemTokens = systemTokens,
        };
    }

    private double ScoreMessageLength(int messageTokens, ICollection<string> factors)
    {
        if (messageTokens >= _scoring.HighComplexityTokenThreshold)
        {
            factors.Add($"message-tokens:{messageTokens}:high");
            return 0.7;
        }

        if (messageTokens >= _scoring.MediumComplexityTokenThreshold)
        {
            factors.Add($"message-tokens:{messageTokens}:medium");
            return 0.35;
        }

        if (messageTokens > 0)
        {
            factors.Add($"message-tokens:{messageTokens}:low");
            return Math.Min(0.25, (double)messageTokens / _scoring.MediumComplexityTokenThreshold * 0.25);
        }

        return 0.0;
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
        var markerCount = MultiStepMarkers.Count(marker => lowered.Contains(marker, StringComparison.Ordinal));
        return markerCount >= 2;
    }
}

/// <summary>
/// Represents the complexity scoring result for a task.
/// </summary>
public sealed record TaskComplexityAssessment
{
    /// <summary>
    /// Gets the normalized complexity score.
    /// </summary>
    public required double Score { get; init; }

    /// <summary>
    /// Gets the contributing scoring factors.
    /// </summary>
    public required IReadOnlyList<string> Factors { get; init; }

    /// <summary>
    /// Gets the estimated user-message token count.
    /// </summary>
    public int MessageTokens { get; init; }

    /// <summary>
    /// Gets the estimated history token count.
    /// </summary>
    public int HistoryTokens { get; init; }

    /// <summary>
    /// Gets the estimated system-message token count.
    /// </summary>
    public int SystemTokens { get; init; }
}
