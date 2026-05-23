namespace LeanKernel.Persistence.Entities;

/// <summary>
/// Represents a persisted scheduled job execution.
/// </summary>
public sealed class ScheduledJobEntity
{
    /// <summary>
    /// Gets or sets the unique execution identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the scheduled job name.
    /// </summary>
    public required string JobName { get; set; }

    /// <summary>
    /// Gets or sets the scheduled occurrence timestamp.
    /// </summary>
    public required DateTimeOffset ScheduledAt { get; set; }

    /// <summary>
    /// Gets or sets when execution started.
    /// </summary>
    public required DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// Gets or sets when execution completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the execution succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the execution result when available.
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Gets or sets the execution error when available.
    /// </summary>
    public string? Error { get; set; }
}
