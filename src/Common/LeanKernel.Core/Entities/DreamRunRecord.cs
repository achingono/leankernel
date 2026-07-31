namespace LeanKernel.Entities;

/// <summary>
/// Persists the outcome of a single Dream cycle execution.
/// </summary>
public sealed class DreamRunRecord
{
    /// <summary>
    /// Gets or sets the unique record identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the Dream source scope identifier.
    /// </summary>
    public string SourceScope { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Dream mode (full, targeted, drain).
    /// </summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON-serialized phase-level status.
    /// </summary>
    public string? PhaseStatusJson { get; set; }

    /// <summary>
    /// Gets or sets the total number of pages processed.
    /// </summary>
    public int TotalPages { get; set; }

    /// <summary>
    /// Gets or sets the number of pages that failed processing.
    /// </summary>
    public int FailedPages { get; set; }

    /// <summary>
    /// Gets or sets the execution start timestamp.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the execution completion timestamp.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the run status (Pending, Running, Completed, Failed, TimedOut).
    /// </summary>
    public string Status { get; set; } = Constants.JobStatus.Pending;

    /// <summary>
    /// Gets or sets the optional FK back to the enrichment job that triggered this run.
    /// </summary>
    public Guid? EnrichmentJobId { get; set; }

    /// <summary>
    /// Gets or sets the record creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}