using Microsoft.Extensions.AI;

namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// Result of a full pipeline execution.
/// </summary>
public sealed class TurnPipelineResult
{
    /// <summary>
    /// The agent's response message.
    /// </summary>
    public ChatMessage? Response { get; init; }

    /// <summary>
    /// Total pipeline execution time.
    /// </summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>
    /// Number of context items admitted.
    /// </summary>
    public int AdmittedCount { get; init; }

    /// <summary>
    /// Number of context items rejected.
    /// </summary>
    public int RejectedCount { get; init; }

    /// <summary>
    /// Whether the pipeline requires continuation.
    /// </summary>
    public bool RequiresContinuation { get; init; }

    /// <summary>
    /// The admission trace for diagnostics.
    /// </summary>
    public IReadOnlyList<AdmissionRecord> AdmissionTrace { get; init; } = [];
}