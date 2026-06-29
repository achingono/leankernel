namespace LeanKernel.Abstractions.Enums;

/// <summary>
/// Represents context exclusion reason values.
/// </summary>
public enum ContextExclusionReason
{
    BudgetExhausted,
    LowRelevanceScore,
    DuplicateContent,
    ScopeViolation,
    PolicyExclusion,
    TokenLimitExceeded
}
