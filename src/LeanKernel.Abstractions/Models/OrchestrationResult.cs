namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Captures the result of one coordinator-worker orchestration run.
/// </summary>
public sealed record OrchestrationResult
{
    /// <summary>
    /// Gets the final response returned by the coordinator.
    /// </summary>
    public required string CoordinatorResponse { get; init; }

    /// <summary>
    /// Gets the worker contributions captured during the orchestration run.
    /// </summary>
    public IReadOnlyList<WorkerContribution> WorkerContributions { get; init; } = [];

    /// <summary>
    /// Gets the total orchestration duration.
    /// </summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// Gets the total number of worker invocations executed by the coordinator.
    /// </summary>
    public int TotalWorkerInvocations { get; init; }
}

/// <summary>
/// Captures one worker contribution made during orchestration.
/// </summary>
public sealed record WorkerContribution
{
    /// <summary>
    /// Gets the worker name.
    /// </summary>
    public required string WorkerName { get; init; }

    /// <summary>
    /// Gets the task delegated to the worker.
    /// </summary>
    public required string Task { get; init; }

    /// <summary>
    /// Gets the worker response content.
    /// </summary>
    public required string Response { get; init; }

    /// <summary>
    /// Gets the worker execution duration.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets a value indicating whether the worker completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the worker failure reason when execution was unsuccessful.
    /// </summary>
    public string? Error { get; init; }
}
