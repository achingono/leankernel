using LeanKernel.Core.Enums;

namespace LeanKernel.Thinker.Routing;

/// <summary>
/// A candidate tier/alias in the escalation chain for model selection (FR-3).
/// </summary>
public sealed class RouteCandidate
{
    /// <summary>Human-readable tier label: "small", "medium", "large", or "paid".</summary>
    public required string Tier { get; init; }

    /// <summary>LiteLLM model alias to send when this candidate is selected.</summary>
    public required string Alias { get; init; }

    /// <summary>Whether this candidate uses a paid provider.</summary>
    public bool IsPaid { get; init; }
}

/// <summary>
/// Structured result of a model selection decision, emitted as a selection log entry (FR-7).
/// </summary>
public sealed class SelectionResult
{
    public required string RequestId { get; init; }
    public required TaskComplexity Complexity { get; init; }
    public required string SelectedAlias { get; init; }
    public required string SelectedTier { get; init; }
    public required string SelectionReason { get; init; }

    /// <summary>"free" or "paid" (AC-8).</summary>
    public required string CostBucket { get; init; }

    public required int AttemptCount { get; init; }
    public required IReadOnlyList<string> FallbackPath { get; init; }
    public long LatencyMs { get; init; }
    public int EstimatedInputTokens { get; init; }
    public int ConstraintCount { get; init; }
    public bool QualityGateTriggered { get; init; }
}
