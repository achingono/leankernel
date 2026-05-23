namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Describes the full outcome of response enhancement.
/// </summary>
public sealed record EnhancementResult
{
    /// <summary>
    /// Gets the response produced before enhancement.
    /// </summary>
    public required string OriginalResponse { get; init; }

    /// <summary>
    /// Gets the final response returned after enhancement.
    /// </summary>
    public required string EnhancedResponse { get; init; }

    /// <summary>
    /// Gets a value indicating whether the final response differs from the original response.
    /// </summary>
    public bool WasModified { get; init; }

    /// <summary>
    /// Gets the per-step execution results.
    /// </summary>
    public IReadOnlyList<EnhancementStepResult> Steps { get; init; } = [];

    /// <summary>
    /// Gets the total enhancement duration.
    /// </summary>
    public TimeSpan TotalDuration { get; init; }
}

/// <summary>
/// Describes the outcome of a single enhancement step.
/// </summary>
public sealed record EnhancementStepResult
{
    /// <summary>
    /// Gets the step name.
    /// </summary>
    public required string StepName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the step completed successfully.
    /// </summary>
    public bool Applied { get; init; }

    /// <summary>
    /// Gets a value indicating whether the step modified the response in progress.
    /// </summary>
    public bool Modified { get; init; }

    /// <summary>
    /// Gets the optional step reason.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Gets the time spent running the step.
    /// </summary>
    public TimeSpan Duration { get; init; }
}
