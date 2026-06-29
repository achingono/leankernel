namespace LeanKernel.Abstractions.Enums;

/// <summary>
/// Represents quality outcome values.
/// </summary>
public enum QualityOutcome
{
    Passed,
    FailedEmpty,
    FailedTooShort,
    FailedLowCoverage,
    FailedRefusal,
    Escalated
}
