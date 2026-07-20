using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Builds onboarding directives from detected profile gaps.
/// </summary>
public interface IOnboardingDirectiveBuilder
{
    /// <summary>
    /// Builds a natural-language directive for future onboarding turns.
    /// </summary>
    string BuildDirective(CompletedTurnEvent turnEvent, IReadOnlyList<string> gaps);
}
