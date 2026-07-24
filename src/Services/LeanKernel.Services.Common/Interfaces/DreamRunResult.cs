namespace LeanKernel.Services.Common.Interfaces;

/// <summary>
/// Result of a Dream cycle run.
/// </summary>
public sealed record DreamRunResult(
    string SourceScope,
    string Mode,
    string Status,
    int TotalPages,
    int FailedPages,
    string? PhaseStatusJson);