using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Common.Publishing;

/// <summary>
/// Publishes completed turn events to the learning service.
/// </summary>
public interface ILearningEventPublisher
{
    /// <summary>
    /// Publishes a single completed turn event.
    /// </summary>
    Task PublishAsync(CompletedTurnEvent completedTurn, CancellationToken cancellationToken = default);
}
