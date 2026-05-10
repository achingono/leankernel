using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Services;

/// <summary>
/// Processes failed turns so capability gaps feed the learning loop.
/// </summary>
public sealed class FailureRecoveryStep : ILearningStep
{
    private readonly RequestFailureHandler _failureHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="FailureRecoveryStep" /> class.
    /// </summary>
    /// <param name="failureHandler">The failure handler used to classify failed turns.</param>
    public FailureRecoveryStep(RequestFailureHandler failureHandler)
    {
        _failureHandler = failureHandler;
    }

    /// <inheritdoc />
    public string Name => "failure-recovery";

    /// <inheritdoc />
    public async Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct)
    {
        if (!turnEvent.HasFailure)
            return LearningStepResult.Succeeded(Name, "No failure detected.");

        var exception = new InvalidOperationException(turnEvent.ErrorMessage);
        await _failureHandler.HandleFailureAsync(
            turnEvent.UserMessage.Content,
            turnEvent.AssistantResponse,
            exception,
            ct);

        return LearningStepResult.Succeeded(Name, turnEvent.ErrorType ?? "failure captured");
    }
}
