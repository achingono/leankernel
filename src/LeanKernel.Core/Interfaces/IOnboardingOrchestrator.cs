using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Coordinates onboarding draft, validation, and completion operations.
/// </summary>
public interface IOnboardingOrchestrator
{
    /// <summary>
    /// Gets the current onboarding status.
    /// </summary>
    /// <param name="ct">A token used to cancel the read.</param>
    /// <returns>The current onboarding status.</returns>
    Task<OnboardingStatus> GetStatusAsync(CancellationToken ct);

    /// <summary>
    /// Gets the current onboarding draft configuration.
    /// </summary>
    /// <param name="ct">A token used to cancel the read.</param>
    /// <returns>The current onboarding draft.</returns>
    Task<OnboardingConfigInput> GetDraftAsync(CancellationToken ct);

    /// <summary>
    /// Saves an onboarding draft configuration.
    /// </summary>
    /// <param name="draft">The draft configuration to persist.</param>
    /// <param name="ct">A token used to cancel the write.</param>
    /// <returns>The resulting onboarding status.</returns>
    Task<OnboardingStatus> SaveDraftAsync(OnboardingConfigInput draft, CancellationToken ct);

    /// <summary>
    /// Validates the current onboarding draft.
    /// </summary>
    /// <param name="ct">A token used to cancel validation.</param>
    /// <returns>The aggregate onboarding validation result.</returns>
    Task<OnboardingValidationResult> ValidateAsync(CancellationToken ct);

    /// <summary>
    /// Completes onboarding when validation succeeds.
    /// </summary>
    /// <param name="ct">A token used to cancel completion.</param>
    /// <returns>The onboarding completion result.</returns>
    Task<OnboardingCompletionResult> CompleteAsync(CancellationToken ct);
}
