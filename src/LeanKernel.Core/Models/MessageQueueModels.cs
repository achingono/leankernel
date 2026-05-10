namespace LeanKernel.Core.Models;

/// <summary>
/// A message queued for delivery during active hours.
/// </summary>
public sealed record QueuedMessage
{
    /// <summary>Gets the message identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the channel used for delivery.</summary>
    public required string Channel { get; init; }

    /// <summary>Gets the recipient address or identifier.</summary>
    public required string Recipient { get; init; }

    /// <summary>Gets the message content.</summary>
    public required string Content { get; init; }

    /// <summary>Gets when the message was enqueued.</summary>
    public required DateTime EnqueuedAt { get; init; }

    /// <summary>Gets the scheduled delivery time, if deferred.</summary>
    public DateTime? ScheduledFor { get; init; }

    /// <summary>Gets whether this message bypasses batching.</summary>
    public bool IsUrgent { get; init; }

    /// <summary>Gets the delivery priority.</summary>
    public int Priority { get; init; } = 5;

    /// <summary>Gets whether the message was delivered.</summary>
    public bool IsDelivered { get; init; }

    /// <summary>Gets when the message was delivered.</summary>
    public DateTime? DeliveredAt { get; init; }

    /// <summary>Gets the number of retry attempts.</summary>
    public int RetryAttempts { get; init; }

    /// <summary>Gets the next retry time.</summary>
    public DateTime? NextRetryAt { get; init; }

    /// <summary>Gets the latest delivery error.</summary>
    public string? LastError { get; init; }
}

/// <summary>
/// Result of a message queue operation.
/// </summary>
public sealed class MessageQueueResult
{
    /// <summary>Gets whether the operation succeeded.</summary>
    public required bool Success { get; init; }

    /// <summary>Gets the queued message identifier.</summary>
    public required string MessageId { get; init; }

    /// <summary>Gets an optional reason for failure or scheduling.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets whether the message will be batched for later delivery.</summary>
    public bool WillBeBatched { get; init; }

    /// <summary>Gets the scheduled delivery time, if any.</summary>
    public DateTime? ScheduledDeliveryTime { get; init; }
}

/// <summary>
/// Statistics about the message queue.
/// </summary>
public sealed class MessageQueueStats
{
    /// <summary>Gets the total number of enqueued messages.</summary>
    public int TotalEnqueued { get; init; }

    /// <summary>Gets the number of pending messages.</summary>
    public int PendingMessages { get; init; }

    /// <summary>Gets the number of delivered messages.</summary>
    public int DeliveredMessages { get; init; }

    /// <summary>Gets the number of urgent pending messages.</summary>
    public int UrgentMessages { get; init; }

    /// <summary>Gets the enqueue time for the oldest pending message.</summary>
    public DateTime? OldestMessageAge { get; init; }

    /// <summary>Gets the next batch delivery window.</summary>
    public DateTime? NextBatchWindow { get; init; }
}
