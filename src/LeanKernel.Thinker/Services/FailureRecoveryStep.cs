using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Services;

/// <summary>
/// Processes failed turns so capability gaps feed the learning loop.
/// </summary>
public sealed class FailureRecoveryStep : ILearningStep
{
    private readonly RequestFailureHandler _failureHandler;
    private readonly ICapabilityGapStore _capabilityGapStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="FailureRecoveryStep" /> class.
    /// </summary>
    /// <param name="failureHandler">The failure handler used to classify failed turns.</param>
    /// <param name="capabilityGapStore">The store that persists capability gaps.</param>
    public FailureRecoveryStep(
        RequestFailureHandler failureHandler,
        ICapabilityGapStore capabilityGapStore)
    {
        _failureHandler = failureHandler;
        _capabilityGapStore = capabilityGapStore;
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

        await _capabilityGapStore.AppendAsync(new CapabilityGap
        {
            TurnEventId = turnEvent.Id,
            SessionId = turnEvent.SessionId,
            UserRequest = turnEvent.UserMessage.Content,
            GapType = turnEvent.ErrorType ?? "unknown",
            Description = turnEvent.ErrorMessage ?? turnEvent.AssistantResponse,
            ObservedAt = turnEvent.CompletedAt
        }, ct);

        return LearningStepResult.Succeeded(Name, turnEvent.ErrorType ?? "failure captured");
    }
}
