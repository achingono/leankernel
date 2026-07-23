namespace LeanKernel.Logic.Events;

/// <summary>
/// Subscribes to the event fan-out at flush time.
/// Implementations receive the full batch of collected events for processing.
/// </summary>
public interface IEventSubscriber
{
    /// <summary>
    /// Handles a batch of collected events at flush time.
    /// </summary>
    /// <param name="events">The batch of collected event objects.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleAsync(IReadOnlyList<object> events, CancellationToken ct = default);
}
