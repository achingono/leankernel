using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.AI;

namespace LeanKernel.Agents.Strategies;

/// <summary>
/// Context passed to an agent strategy for invocation.
/// </summary>
public sealed class AgentStrategyContext
{
    public required string SessionId { get; init; }

    public required string TurnId { get; init; }

    public required string UserMessage { get; init; }

    public required string SystemMessage { get; init; }

    public required IReadOnlyList<ConversationTurn> History { get; init; }

    public IReadOnlyList<AITool>? Tools { get; init; }

    public IReadOnlyList<string> AvailableToolNames { get; init; } = [];

    public string? ModelUsed { get; set; }

    /// <summary>
    /// Gets or sets the best-effort token count used by the authoritative strategy response.
    /// </summary>
    public int? TokensUsed { get; set; }

    public RoutingDecision? RoutingDecision { get; set; }

    /// <summary>
    /// Gets or sets the orchestration result when coordinator-worker execution is used.
    /// </summary>
    public OrchestrationResult? OrchestrationResult { get; set; }

    public QualityOutcome QualityOutcome { get; set; } = QualityOutcome.Passed;

    public QualityGateResult? QualityGateResult { get; set; }
}
