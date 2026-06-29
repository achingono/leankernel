using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Defines a sink for publishing turn events for observability and processing.
/// </summary>
public interface ITurnEventSink
{
    /// <summary>
    /// Publishes a turn event.
    /// </summary>
    /// <param name="turnEvent">The turn event to publish.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishAsync(TurnEvent turnEvent, CancellationToken ct = default);
}
