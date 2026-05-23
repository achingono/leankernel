namespace LeanKernel.Abstractions.Enums;

public enum QualityOutcome
{
    Passed,
    FailedEmpty,
    FailedTooShort,
    FailedLowCoverage,
    FailedRefusal,
    Escalated
}
