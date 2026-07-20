using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Common.Queue;

/// <summary>
/// Asynchronous queue abstraction for completed turn events.
/// </summary>
public interface ITurnEventQueue
{
    /// <summary>
    /// Attempts to enqueue a completed turn event.
    /// </summary>
    ValueTask<bool> EnqueueAsync(CompletedTurnEvent completedTurn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dequeues events as they become available.
    /// </summary>
    IAsyncEnumerable<CompletedTurnEvent> DequeueAllAsync(CancellationToken cancellationToken = default);
}
