using LeanKernel.Events;

namespace LeanKernel.Services.Learning;

/// <summary>
/// Consumes turn-completed events from the learning pipeline queue.
/// </summary>
public interface ITurnEventConsumer
{
    /// <summary>
    /// Attempts to dequeue a turn-completed event.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dequeued event, or <c>null</c> if the queue is complete.</returns>
    ValueTask<TurnCompletedEvent?> TryDequeueAsync(CancellationToken ct = default);
}