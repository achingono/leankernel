namespace LeanKernel.Abstractions.Enums;

public enum ContextExclusionReason
{
    BudgetExhausted,
    LowRelevanceScore,
    DuplicateContent,
    ScopeViolation,
    PolicyExclusion,
    TokenLimitExceeded
}
