namespace LeanKernel.Agents.Orchestration;

/// <summary>
/// Represents the decision to use coordinator-worker orchestration for a request.
/// </summary>
public sealed record OrchestrationDecision
{
    /// <summary>
    /// Gets a value indicating whether the request should use orchestration.
    /// </summary>
    public required bool ShouldOrchestrate { get; init; }

    /// <summary>
    /// Gets the human-readable reason for the decision.
    /// </summary>
    public required string Reason { get; init; }
}
