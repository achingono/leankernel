namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Captures retrieval decision diagnostics for a single retrieval operation.
/// </summary>
public sealed record RetrievalDiagnostics
{
    /// <summary>
    /// Gets the session identifier associated with the retrieval operation.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets the turn identifier associated with the retrieval operation.
    /// </summary>
    public required string TurnId { get; init; }

    /// <summary>
    /// Gets the per-candidate retrieval decisions.
    /// </summary>
    public IReadOnlyList<RetrievalCandidateDecision> Decisions { get; init; } = [];

    /// <summary>
    /// Gets the total number of candidates considered.
    /// </summary>
    public int TotalConsidered { get; init; }

    /// <summary>
    /// Gets the total number of admitted candidates.
    /// </summary>
    public int TotalAdmitted { get; init; }

    /// <summary>
    /// Gets the total number of candidates excluded by scope policy.
    /// </summary>
    public int TotalExcludedByScope { get; init; }

    /// <summary>
    /// Gets the total number of candidates excluded by score thresholds.
    /// </summary>
    public int TotalExcludedByScore { get; init; }

    /// <summary>
    /// Gets the effective scope applied to retrieval.
    /// </summary>
    public string EffectiveScope { get; init; } = "global";

    /// <summary>
    /// Gets the expanded entities used during retrieval.
    /// </summary>
    public IReadOnlyList<string> ExpandedEntities { get; init; } = [];
}

/// <summary>
/// Records the admission decision for a single retrieval candidate.
/// </summary>
public sealed record RetrievalCandidateDecision
{
    /// <summary>
    /// Gets the candidate key.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the candidate source.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets the original score assigned by the backing knowledge service.
    /// </summary>
    public double OriginalScore { get; init; }

    /// <summary>
    /// Gets the adjusted score after entity-aware boosting.
    /// </summary>
    public double AdjustedScore { get; init; }

    /// <summary>
    /// Gets a value indicating whether the candidate was admitted.
    /// </summary>
    public bool Admitted { get; init; }

    /// <summary>
    /// Gets the exclusion reason when the candidate is not admitted.
    /// </summary>
    public string? ExclusionReason { get; init; }
}
