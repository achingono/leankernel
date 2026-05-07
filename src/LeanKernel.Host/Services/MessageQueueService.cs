using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Host.Services;

/// <summary>
/// Queues messages for delivery during active hours (batching for quiet hours).
/// </summary>
public interface IMessageQueue
{
    /// <summary>
    /// Enqueue a message for delivery (either now or later if in quiet hours).
    /// </summary>
    Task<MessageQueueResult> EnqueueAsync(
        QueuedMessage message,
        bool isUrgent = false,
        CancellationToken ct = default);

    /// <summary>
    /// Get all pending messages ready for delivery.
    /// </summary>
    Task<IReadOnlyList<QueuedMessage>> GetReadyMessagesAsync(CancellationToken ct = default);

    /// <summary>
    /// Mark a message as delivered.
    /// </summary>
    Task MarkDeliveredAsync(string messageId, CancellationToken ct = default);

    /// <summary>
    /// Mark a message as failed with error details.
    /// </summary>
    Task MarkFailedAsync(string messageId, string error, CancellationToken ct = default);

    /// <summary>
    /// Mark a message for retry with scheduled time.
    /// </summary>
    Task MarkRetryableAsync(string messageId, string error, DateTime nextRetryAt, CancellationToken ct = default);

    /// <summary>
    /// Get queue statistics.
    /// </summary>
    Task<MessageQueueStats> GetStatsAsync(CancellationToken ct = default);
}

/// <summary>
/// A message queued for delivery during active hours.
/// </summary>
public sealed record QueuedMessage
{
    public required string Id { get; init; }
    public required string Channel { get; init; }
    public required string Recipient { get; init; }
    public required string Content { get; init; }
    public required DateTime EnqueuedAt { get; init; }
    public DateTime? ScheduledFor { get; init; }
    public bool IsUrgent { get; init; }
    public int Priority { get; init; } = 5;
    public bool IsDelivered { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public int RetryAttempts { get; init; }
    public DateTime? NextRetryAt { get; init; }
    public string? LastError { get; init; }
}

/// <summary>
/// Result of queuing operation.
/// </summary>
public sealed class MessageQueueResult
{
    public required bool Success { get; init; }
    public required string MessageId { get; init; }
    public string? Reason { get; init; }
    public bool WillBeBatched { get; init; }
    public DateTime? ScheduledDeliveryTime { get; init; }
}

/// <summary>
/// Statistics about the message queue.
/// </summary>
public sealed class MessageQueueStats
{
    public int TotalEnqueued { get; init; }
    public int PendingMessages { get; init; }
    public int DeliveredMessages { get; init; }
    public int UrgentMessages { get; init; }
    public DateTime? OldestMessageAge { get; init; }
    public DateTime? NextBatchWindow { get; init; }
}

/// <summary>
/// In-memory message queue service that respects quiet hours.
/// During quiet hours, non-urgent messages are batched for next active window.
/// Urgent messages are always delivered immediately.
/// </summary>
public sealed class MessageQueueService : IMessageQueue
{
    private readonly ITimeBoundaryService _timeBoundary;
    private readonly ILogger<MessageQueueService> _logger;
    private readonly ConcurrentDictionary<string, QueuedMessage> _messages;
    private int _messageCounter;

    public MessageQueueService(
        ITimeBoundaryService timeBoundary,
        ILogger<MessageQueueService> logger)
    {
        _timeBoundary = timeBoundary;
        _logger = logger;
        _messages = new ConcurrentDictionary<string, QueuedMessage>();
    }

    public Task<MessageQueueResult> EnqueueAsync(
        QueuedMessage message,
        bool isUrgent = false,
        CancellationToken ct = default)
    {
        var isInActiveHours = _timeBoundary.IsInActiveHours();
        var messageId = message.Id ?? GenerateMessageId();
        
        var queuedMessage = message with
        {
            Id = messageId,
            EnqueuedAt = DateTime.UtcNow,
            IsUrgent = isUrgent,
            ScheduledFor = !isInActiveHours && !isUrgent 
                ? _timeBoundary.GetNextActiveWindow() 
                : null
        };

        var added = _messages.TryAdd(messageId, queuedMessage);
        if (!added)
        {
            _logger.LogWarning("Message {MessageId} already exists in queue", messageId);
            return Task.FromResult(new MessageQueueResult
            {
                Success = false,
                MessageId = messageId,
                Reason = "Message ID already in queue"
            });
        }

        var willBeBatched = !isInActiveHours && !isUrgent;
        var scheduledTime = willBeBatched ? queuedMessage.ScheduledFor : null;

        _logger.LogInformation(
            "Message {MessageId} enqueued for {Channel} (urgent={Urgent}, batched={Batched}, scheduled={ScheduledTime})",
            messageId, message.Channel, isUrgent, willBeBatched, scheduledTime);

        return Task.FromResult(new MessageQueueResult
        {
            Success = true,
            MessageId = messageId,
            WillBeBatched = willBeBatched,
            ScheduledDeliveryTime = scheduledTime
        });
    }

    public Task<IReadOnlyList<QueuedMessage>> GetReadyMessagesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var isInActiveHours = _timeBoundary.IsInActiveHours();

        var readyMessages = _messages.Values
            .Where(m => !m.IsDelivered && (
                m.IsUrgent ||
                isInActiveHours ||
                (m.ScheduledFor.HasValue && m.ScheduledFor.Value <= now)
            ))
            .OrderByDescending(m => m.Priority)
            .ThenByDescending(m => m.IsUrgent)
            .ThenBy(m => m.EnqueuedAt)
            .ToList();

        _logger.LogDebug("Found {Count} ready messages (active hours: {Active})", readyMessages.Count, isInActiveHours);

        return Task.FromResult((IReadOnlyList<QueuedMessage>)readyMessages);
    }

    public Task MarkDeliveredAsync(string messageId, CancellationToken ct = default)
    {
        if (!_messages.TryGetValue(messageId, out var message))
        {
            _logger.LogWarning("Message {MessageId} not found in queue", messageId);
            return Task.CompletedTask;
        }

        var deliveredMessage = message with
        {
            IsDelivered = true,
            DeliveredAt = DateTime.UtcNow
        };

        if (_messages.TryUpdate(messageId, deliveredMessage, message))
        {
            _logger.LogInformation("Message {MessageId} marked as delivered", messageId);
        }

        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(string messageId, string error, CancellationToken ct = default)
    {
        if (!_messages.TryGetValue(messageId, out var message))
        {
            _logger.LogWarning("Message {MessageId} not found in queue", messageId);
            return Task.CompletedTask;
        }

        var failedMessage = message with
        {
            LastError = error,
            RetryAttempts = message.RetryAttempts + 1
        };

        if (_messages.TryUpdate(messageId, failedMessage, message))
        {
            _logger.LogWarning(
                "Message {MessageId} marked as failed (attempt {Attempt}): {Error}",
                messageId,
                failedMessage.RetryAttempts,
                error);
        }

        return Task.CompletedTask;
    }

    public Task MarkRetryableAsync(string messageId, string error, DateTime nextRetryAt, CancellationToken ct = default)
    {
        if (!_messages.TryGetValue(messageId, out var message))
        {
            _logger.LogWarning("Message {MessageId} not found in queue", messageId);
            return Task.CompletedTask;
        }

        var retryMessage = message with
        {
            LastError = error,
            NextRetryAt = nextRetryAt,
            RetryAttempts = message.RetryAttempts + 1
        };

        if (_messages.TryUpdate(messageId, retryMessage, message))
        {
            _logger.LogInformation(
                "Message {MessageId} scheduled for retry at {RetryAt} (attempt {Attempt}): {Error}",
                messageId,
                nextRetryAt,
                retryMessage.RetryAttempts,
                error);
        }

        return Task.CompletedTask;
    }

    public Task<MessageQueueStats> GetStatsAsync(CancellationToken ct = default)
    {
        var totalMessages = _messages.Values.ToList();
        var pendingMessages = totalMessages.Where(m => !m.IsDelivered).ToList();
        var deliveredMessages = totalMessages.Where(m => m.IsDelivered).ToList();
        var urgentMessages = pendingMessages.Count(m => m.IsUrgent);

        var oldestPending = pendingMessages.OrderBy(m => m.EnqueuedAt).FirstOrDefault();
        var nextWindow = _timeBoundary.GetNextActiveWindow();

        return Task.FromResult(new MessageQueueStats
        {
            TotalEnqueued = totalMessages.Count,
            PendingMessages = pendingMessages.Count,
            DeliveredMessages = deliveredMessages.Count,
            UrgentMessages = urgentMessages,
            OldestMessageAge = oldestPending?.EnqueuedAt,
            NextBatchWindow = nextWindow
        });
    }

    private string GenerateMessageId()
    {
        return $"msg_{++_messageCounter}_{Guid.NewGuid():N}";
    }
}
