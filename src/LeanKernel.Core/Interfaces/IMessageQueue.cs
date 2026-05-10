using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Queues outbound messages for immediate or deferred delivery.
/// </summary>
public interface IMessageQueue
{
    /// <summary>
    /// Enqueues a message for delivery, either immediately or at a later active window.
    /// </summary>
    /// <param name="message">The message to enqueue.</param>
    /// <param name="isUrgent">Whether the message bypasses quiet-hour batching.</param>
    /// <param name="ct">A token used to cancel the enqueue operation.</param>
    /// <returns>The enqueue result including the assigned message identifier and scheduling state.</returns>
    Task<MessageQueueResult> EnqueueAsync(
        QueuedMessage message,
        bool isUrgent = false,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all pending messages that are ready for delivery.
    /// </summary>
    /// <param name="ct">A token used to cancel the query.</param>
    /// <returns>The messages ready to send.</returns>
    Task<IReadOnlyList<QueuedMessage>> GetReadyMessagesAsync(CancellationToken ct = default);

    /// <summary>
    /// Marks a message as delivered.
    /// </summary>
    /// <param name="messageId">The queued message identifier.</param>
    /// <param name="ct">A token used to cancel the update.</param>
    Task MarkDeliveredAsync(string messageId, CancellationToken ct = default);

    /// <summary>
    /// Marks a message as failed.
    /// </summary>
    /// <param name="messageId">The queued message identifier.</param>
    /// <param name="error">The delivery error.</param>
    /// <param name="ct">A token used to cancel the update.</param>
    Task MarkFailedAsync(string messageId, string error, CancellationToken ct = default);

    /// <summary>
    /// Marks a message for retry at a specific time.
    /// </summary>
    /// <param name="messageId">The queued message identifier.</param>
    /// <param name="error">The delivery error that caused the retry.</param>
    /// <param name="nextRetryAt">The next retry time in UTC.</param>
    /// <param name="ct">A token used to cancel the update.</param>
    Task MarkRetryableAsync(string messageId, string error, DateTime nextRetryAt, CancellationToken ct = default);

    /// <summary>
    /// Gets queue statistics for diagnostics and administration.
    /// </summary>
    /// <param name="ct">A token used to cancel the query.</param>
    /// <returns>The current queue statistics.</returns>
    Task<MessageQueueStats> GetStatsAsync(CancellationToken ct = default);
}
