namespace LeanKernel.Commander.Queue.Data;

/// <summary>
/// Entity for persisting queued messages to database.
/// </summary>
public sealed class QueuedMessageEntity
{
    /// <summary>
    /// Gets or sets the queue message identifier.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the target channel name.
    /// </summary>
    public string Channel { get; set; } = "";

    /// <summary>
    /// Gets or sets the channel-specific recipient identifier.
    /// </summary>
    public string Recipient { get; set; } = "";

    /// <summary>
    /// Gets or sets the message content to deliver.
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Gets or sets when the message entered the queue.
    /// </summary>
    public DateTime EnqueuedAt { get; set; }

    /// <summary>
    /// Gets or sets when the message should next be considered for delivery.
    /// </summary>
    public DateTime? ScheduledFor { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the message bypasses quiet-hour batching.
    /// </summary>
    public bool IsUrgent { get; set; }

    /// <summary>
    /// Gets or sets the delivery priority.
    /// </summary>
    public int Priority { get; set; } = 5;

    /// <summary>
    /// Gets or sets a value indicating whether the message has been delivered.
    /// </summary>
    public bool IsDelivered { get; set; }

    /// <summary>
    /// Gets or sets when the message was delivered.
    /// </summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>
    /// Gets or sets the number of failed delivery attempts.
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// Gets or sets when the next retry should occur.
    /// </summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>
    /// Gets or sets the last delivery error message.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets when the row was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when the row was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
