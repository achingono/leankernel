namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents the outcome of onboarding-gap detection for a turn.
/// </summary>
public sealed record OnboardingResult
{
    /// <summary>
    /// Gets a value indicating whether any onboarding gaps were detected.
    /// </summary>
    public bool HasGaps { get; init; }

    /// <summary>
    /// Gets the detected identity gaps.
    /// </summary>
    public IReadOnlyList<IdentityGap> Gaps { get; init; } = [];

    /// <summary>
    /// Gets the onboarding directive to include in prompt construction, if any.
    /// </summary>
    public string? OnboardingDirective { get; init; }
}

/// <summary>
/// Represents a single identity gap that may require onboarding follow-up.
/// </summary>
public sealed record IdentityGap
{
    /// <summary>
    /// Gets the affected field name.
    /// </summary>
    public required string FieldName { get; init; }

    /// <summary>
    /// Gets the deterministic gap code.
    /// </summary>
    public required string GapCode { get; init; }

    /// <summary>
    /// Gets the optional explanation for the gap.
    /// </summary>
    public string? Reason { get; init; }
}
