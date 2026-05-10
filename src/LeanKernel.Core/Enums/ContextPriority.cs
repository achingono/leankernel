namespace LeanKernel.Core.Enums;

/// <summary>
/// Represents the available context priority values.
/// </summary>
public enum ContextPriority
{
    /// <summary>
    /// Must always be included in context.
    /// </summary>
    Critical,

    /// <summary>
    /// Should be included when budget allows.
    /// </summary>
    High,

    /// <summary>
    /// Normal-priority context candidate.
    /// </summary>
    Medium,

    /// <summary>
    /// Low-priority context candidate.
    /// </summary>
    Low,

    /// <summary>
    /// Candidate should be excluded from context.
    /// </summary>
    Exclude
}
