using LeanKernel.Abstractions.Enums;

namespace LeanKernel.Abstractions.Models;

public sealed record QualityGateResult
{
    public required QualityOutcome Outcome { get; init; }

    public required bool Passed { get; init; }

    public IReadOnlyList<QualityCheckResult> Checks { get; init; } = [];

    public string? FailureReason { get; init; }

    public double OverallScore { get; init; }
}

public sealed record QualityCheckResult
{
    public required string CheckName { get; init; }

    public required bool Passed { get; init; }

    public double Score { get; init; }

    public string? Details { get; init; }
}
