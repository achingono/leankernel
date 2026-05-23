using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Detects missing or weak identity information that should trigger additive onboarding guidance.
/// </summary>
public interface IOnboardingDetector
{
    /// <summary>
    /// Detects onboarding gaps for the supplied identity context.
    /// </summary>
    /// <param name="identity">The identity context to evaluate.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The onboarding gap analysis result.</returns>
    Task<OnboardingResult> DetectGapsAsync(IdentityContext identity, CancellationToken ct = default);
}
