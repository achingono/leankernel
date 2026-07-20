using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Detects missing onboarding profile fields from user turn content.
/// </summary>
public interface IOnboardingGapDetector
{
    /// <summary>
    /// Returns a list of missing onboarding fields for the provided turn.
    /// </summary>
    IReadOnlyList<string> DetectGaps(CompletedTurnEvent turnEvent);
}
