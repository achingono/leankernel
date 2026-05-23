namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents the outcome of a scheduled job execution.
/// </summary>
public sealed record ScheduledJobExecution
{
    /// <summary>
    /// Gets the job name.
    /// </summary>
    public required string JobName { get; init; }

    /// <summary>
    /// Gets the scheduled occurrence that triggered this execution.
    /// </summary>
    public required DateTimeOffset ScheduledAt { get; init; }

    /// <summary>
    /// Gets the timestamp when execution started.
    /// </summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Gets the timestamp when execution completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Gets a value indicating whether the execution completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the execution result when available.
    /// </summary>
    public string? Result { get; init; }

    /// <summary>
    /// Gets the execution error when available.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the elapsed execution duration.
    /// </summary>
    public TimeSpan Duration => (CompletedAt ?? DateTimeOffset.UtcNow) - StartedAt;
}
