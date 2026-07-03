namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents completion-evaluation output for a turn response.
/// </summary>
public sealed record TaskCompletionAssessment(
    bool IsComplete,
    double Confidence,
    string? ProgressNote,
    string Reason);
