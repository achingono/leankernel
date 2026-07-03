using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Publishes and subscribes to in-flight turn progress updates.
/// </summary>
public interface ITurnProgressBroker
{
    /// <summary>
    /// Subscribes a handler for a specific session identifier.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="handler">The update handler.</param>
    /// <returns>A disposable subscription.</returns>
    IDisposable Subscribe(string sessionId, Func<TurnProgressUpdate, Task> handler);

    /// <summary>
    /// Publishes a turn progress update.
    /// </summary>
    /// <param name="update">The update payload.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing asynchronous publication.</returns>
    Task PublishAsync(TurnProgressUpdate update, CancellationToken ct = default);
}
