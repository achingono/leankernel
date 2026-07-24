using LeanKernel.Events;

namespace LeanKernel.Services.Learning;

/// <summary>
/// Produces turn-completed events for downstream learning pipeline consumption.
/// </summary>
public interface ITurnEventProducer
{
    /// <summary>
    /// Enqueues a turn-completed event for processing.
    /// </summary>
    /// <param name="turnEvent">The turn event payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> representing the enqueue operation.</returns>
    ValueTask EnqueueAsync(TurnCompletedEvent turnEvent, CancellationToken ct = default);
}