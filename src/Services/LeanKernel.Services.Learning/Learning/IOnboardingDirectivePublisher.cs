using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Publishes generated onboarding directives to persistent memory.
/// </summary>
public interface IOnboardingDirectivePublisher
{
    /// <summary>
    /// Publishes a directive for the supplied completed turn.
    /// </summary>
    Task PublishAsync(CompletedTurnEvent turnEvent, string directive, CancellationToken cancellationToken = default);
}
