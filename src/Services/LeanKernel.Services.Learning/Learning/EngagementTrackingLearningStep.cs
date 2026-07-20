using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Computes lightweight engagement signals for each completed turn.
/// </summary>
/// <param name="coordinator">Persists computed engagement signals.</param>
public sealed class EngagementTrackingLearningStep(IKnowledgePageUpdateCoordinator coordinator) : ILearningPipelineStep
{
    /// <inheritdoc />
    public string StepName => "engagement-tracking";

    /// <inheritdoc />
    public async Task ExecuteAsync(CompletedTurnEvent turnEvent, CancellationToken cancellationToken = default)
    {
        var requestLength = turnEvent.RequestMessages.Sum(static message => message.Text.Length);
        var responseLength = turnEvent.ResponseMessages.Sum(static message => message.Text.Length);
        var signal = $"request_chars={requestLength};response_chars={responseLength};messages={turnEvent.RequestMessages.Count + turnEvent.ResponseMessages.Count}";
        await coordinator.WriteEngagementSignalAsync(turnEvent, signal, cancellationToken).ConfigureAwait(false);
    }
}
