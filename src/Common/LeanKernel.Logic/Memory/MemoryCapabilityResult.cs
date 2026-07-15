namespace LeanKernel.Logic.Memory;

/// <summary>
/// Describes the result of the Memory capability pre-check.
/// </summary>
public sealed class MemoryCapabilityResult
{
    /// <summary>The probe outcome.</summary>
    public MemoryCapabilityStatus Status { get; init; }

    /// <summary>Whether <c>wiki_search</c> should be registered.</summary>
    public bool CanSearch { get; init; }

    /// <summary>Whether <c>wiki_read</c> should be registered.</summary>
    public bool CanRead { get; init; }

    /// <summary>Whether <c>wiki_write</c> should be registered.</summary>
    public bool CanWrite { get; init; }

    /// <summary>Human-readable diagnostic message.</summary>
    public string Reason { get; init; } = string.Empty;
}