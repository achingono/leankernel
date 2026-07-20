using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Common.Publishing;

/// <summary>
/// No-op learning publisher used when learning integration is disabled.
/// </summary>
public sealed class NoOpLearningEventPublisher : ILearningEventPublisher
{
    /// <inheritdoc />
    public Task PublishAsync(CompletedTurnEvent completedTurn, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
