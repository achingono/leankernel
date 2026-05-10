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
    /// <summary>
    /// Gets or sets the request id.
    /// </summary>
    public required string RequestId { get; init; }
    /// <summary>
    /// Gets or sets the complexity.
    /// </summary>
    public required TaskComplexity Complexity { get; init; }
    /// <summary>
    /// Gets or sets the selected alias.
    /// </summary>
    public required string SelectedAlias { get; init; }
    /// <summary>
    /// Gets or sets the selected tier.
    /// </summary>
    public required string SelectedTier { get; init; }
    /// <summary>
    /// Gets or sets the selection reason.
    /// </summary>
    public required string SelectionReason { get; init; }

    /// <summary>"free" or "paid" (AC-8).</summary>
    public required string CostBucket { get; init; }

    /// <summary>
    /// Gets or sets the attempt count.
    /// </summary>
    public required int AttemptCount { get; init; }
    /// <summary>
    /// Gets or sets the fallback path.
    /// </summary>
    public required IReadOnlyList<string> FallbackPath { get; init; }
    /// <summary>
    /// Gets or sets the latency ms.
    /// </summary>
    public long LatencyMs { get; init; }
    /// <summary>
    /// Gets or sets the estimated input tokens.
    /// </summary>
    public int EstimatedInputTokens { get; init; }
    /// <summary>
    /// Gets or sets the constraint count.
    /// </summary>
    public int ConstraintCount { get; init; }
    /// <summary>
    /// Gets or sets the quality gate triggered.
    /// </summary>
    public bool QualityGateTriggered { get; init; }

    /// <summary>UTC timestamp when the routing decision was made.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
