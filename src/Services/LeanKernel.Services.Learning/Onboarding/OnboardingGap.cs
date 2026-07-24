namespace LeanKernel.Services.Learning.Onboarding;

/// <summary>
/// Represents a detected gap in the user onboarding process.
/// </summary>
/// <param name="GapType">The type of onboarding gap identified.</param>
/// <param name="SuggestedDirective">The suggested directive to address the gap.</param>
/// <param name="Priority">The priority level of the gap.</param>
public sealed record OnboardingGap(
    string GapType,
    string SuggestedDirective,
    int Priority);