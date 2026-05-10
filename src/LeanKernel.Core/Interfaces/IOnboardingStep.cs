using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Represents a configurable onboarding step.
/// </summary>
public interface IOnboardingStep
{
    /// <summary>
    /// Gets the stable step name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Initializes any files or state required by the step.
    /// </summary>
    /// <param name="ct">A token used to cancel initialization.</param>
    /// <returns>The initialization result.</returns>
    Task<ConfigurationStepResult> InitializeAsync(CancellationToken ct);

    /// <summary>
    /// Validates the step's current state.
    /// </summary>
    /// <param name="ct">A token used to cancel validation.</param>
    /// <returns>The validation result.</returns>
    Task<ConfigurationStepResult> ValidateAsync(CancellationToken ct);

    /// <summary>
    /// Gets the current markdown representation for the step, when applicable.
    /// </summary>
    /// <param name="ct">A token used to cancel the read.</param>
    /// <returns>The current markdown content.</returns>
    Task<string> GetMarkdownAsync(CancellationToken ct);
}
