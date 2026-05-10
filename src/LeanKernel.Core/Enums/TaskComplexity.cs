namespace LeanKernel.Core.Enums;

/// <summary>
/// Classification of request complexity used by the intelligent routing pipeline (FR-1).
/// </summary>
public enum TaskComplexity
{
    /// <summary>
    /// Small request suitable for low-cost model aliases.
    /// </summary>
    Small,

    /// <summary>
    /// Medium request requiring a stronger model alias.
    /// </summary>
    Medium,

    /// <summary>
    /// Large request requiring the strongest configured model alias.
    /// </summary>
    Large
}
