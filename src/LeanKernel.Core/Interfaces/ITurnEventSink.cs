using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Accepts completed turn events for durable post-turn processing.
/// </summary>
public interface ITurnEventSink
{
    /// <summary>
    /// Enqueues a completed turn event for background self-improvement.
    /// </summary>
    /// <param name="turnEvent">The completed turn event.</param>
    /// <param name="ct">A token used to cancel enqueueing.</param>
    Task EnqueueAsync(TurnEvent turnEvent, CancellationToken ct);
}
