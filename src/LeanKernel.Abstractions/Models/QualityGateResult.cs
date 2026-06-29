using LeanKernel.Abstractions.Enums;

namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents the result of a quality gate check.
/// </summary>
public sealed record QualityGateResult
{
    /// <summary>
    /// Gets the outcome of the quality gate.
    /// </summary>
    public required QualityOutcome Outcome { get; init; }

    /// <summary>
    /// Gets a value indicating whether the quality gate passed.
    /// </summary>
    public required bool Passed { get; init; }

    /// <summary>
    /// Gets the list of individual quality check results.
    /// </summary>
    public IReadOnlyList<QualityCheckResult> Checks { get; init; } = [];

    /// <summary>
    /// Gets the reason for failure, if any.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Gets the overall quality score.
    /// </summary>
    public double OverallScore { get; init; }
}

/// <summary>
/// Represents the result of an individual quality check.
/// </summary>
public sealed record QualityCheckResult
{
    /// <summary>
    /// Gets the name of the check.
    /// </summary>
    public required string CheckName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the check passed.
    /// </summary>
    public required bool Passed { get; init; }

    /// <summary>
    /// Gets the score of the check.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Gets the details of the check result.
    /// </summary>
    public string? Details { get; init; }
}
