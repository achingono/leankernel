using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Persists onboarding completion state.
/// </summary>
public interface IOnboardingStateStore
{
    /// <summary>
    /// Gets the current onboarding state document.
    /// </summary>
    /// <param name="ct">A token used to cancel the read.</param>
    /// <returns>The current onboarding state document.</returns>
    Task<OnboardingStateDocument> GetAsync(CancellationToken ct);

    /// <summary>
    /// Gets whether onboarding has completed.
    /// </summary>
    /// <param name="ct">A token used to cancel the read.</param>
    /// <returns><see langword="true" /> when onboarding is complete; otherwise <see langword="false" />.</returns>
    Task<bool> IsCompletedAsync(CancellationToken ct);

    /// <summary>
    /// Marks onboarding as started or in progress.
    /// </summary>
    /// <param name="ct">A token used to cancel the update.</param>
    Task MarkInProgressAsync(CancellationToken ct);

    /// <summary>
    /// Marks onboarding as completed.
    /// </summary>
    /// <param name="ct">A token used to cancel the update.</param>
    Task MarkCompletedAsync(CancellationToken ct);
}
