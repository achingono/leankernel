namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Describes how the runtime should degrade for the current provider-health state.
/// </summary>
public sealed record GracefulDegradationDecision
{
    /// <summary>
    /// Gets a value indicating whether model execution is allowed.
    /// </summary>
    public bool AllowModelExecution { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether live knowledge retrieval should be skipped.
    /// </summary>
    public bool SkipKnowledgeRetrieval { get; init; }

    /// <summary>
    /// Gets a value indicating whether persistence is degraded.
    /// </summary>
    public bool PersistenceDegraded { get; init; }

    /// <summary>
    /// Gets the user-facing message to return when execution is blocked.
    /// </summary>
    public string? UserMessage { get; init; }

    /// <summary>
    /// Gets runtime warnings that should accompany the response.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether any degradation is active.
    /// </summary>
    public bool IsDegraded => !AllowModelExecution || SkipKnowledgeRetrieval || PersistenceDegraded || Warnings.Count > 0;
}
