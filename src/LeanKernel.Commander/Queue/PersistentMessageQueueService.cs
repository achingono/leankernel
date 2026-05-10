using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LeanKernel.Commander.Queue.Data;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Commander.Queue;

/// <summary>
/// Message queue service with persistent database storage.
/// Wraps the in-memory queue and syncs to SQLite for durability.
/// </summary>
public sealed class PersistentMessageQueueService : IMessageQueue
{
    private readonly IMessageQueue _inMemoryQueue;
    private readonly MessageQueueDbContext _dbContext;
    private readonly ILogger<PersistentMessageQueueService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PersistentMessageQueueService" /> class.
    /// </summary>
    /// <param name="inMemoryQueue">The in-memory queue that provides immediate scheduling behavior.</param>
    /// <param name="dbContext">The database context used to persist queued message state.</param>
    /// <param name="logger">The logger used for persistence diagnostics.</param>
    public PersistentMessageQueueService(
        IMessageQueue inMemoryQueue,
        MessageQueueDbContext dbContext,
        ILogger<PersistentMessageQueueService> logger)
    {
        _inMemoryQueue = inMemoryQueue;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MessageQueueResult> EnqueueAsync(
        QueuedMessage message,
        bool isUrgent = false,
        CancellationToken ct = default)
    {
        try
        {
            // Enqueue to in-memory queue
            var result = await _inMemoryQueue.EnqueueAsync(message, isUrgent, ct);

            if (!result.Success)
            {
                _logger.LogWarning("Failed to enqueue message {MessageId} to in-memory queue: {Reason}",
                    result.MessageId, result.Reason);
                return result;
            }

            // Persist to database
            var entity = new QueuedMessageEntity
            {
                Id = message.Id,
                Channel = message.Channel,
                Recipient = message.Recipient,
                Content = message.Content,
                EnqueuedAt = message.EnqueuedAt,
                ScheduledFor = message.ScheduledFor,
                IsUrgent = message.IsUrgent || isUrgent,
                Priority = message.Priority,
                IsDelivered = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.QueuedMessages.Add(entity);
            await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation("Message {MessageId} enqueued and persisted to database", message.Id);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enqueueing message {MessageId}", message.Id);
            return new MessageQueueResult
            {
                Success = false,
                MessageId = message.Id,
                Reason = $"Database persistence failed: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueuedMessage>> GetReadyMessagesAsync(CancellationToken ct = default)
    {
        try
        {
            // Get ready messages from in-memory queue
            var messages = await _inMemoryQueue.GetReadyMessagesAsync(ct);
            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving ready messages");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task MarkDeliveredAsync(string messageId, CancellationToken ct = default)
    {
        try
        {
            // Mark as delivered in in-memory queue
            await _inMemoryQueue.MarkDeliveredAsync(messageId, ct);

            // Update database
            var entity = await _dbContext.QueuedMessages
                .FirstOrDefaultAsync(m => m.Id == messageId, ct);

            if (entity != null)
            {
                entity.IsDelivered = true;
                entity.DeliveredAt = DateTime.UtcNow;
                entity.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(ct);

                _logger.LogInformation("Message {MessageId} marked as delivered", messageId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking message {MessageId} as delivered", messageId);
        }
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(string messageId, string error, CancellationToken ct = default)
    {
        try
        {
            // Mark as failed in in-memory queue
            await _inMemoryQueue.MarkFailedAsync(messageId, error, ct);

            // Update database
            var entity = await _dbContext.QueuedMessages
                .FirstOrDefaultAsync(m => m.Id == messageId, ct);

            if (entity != null)
            {
                entity.LastError = error;
                entity.RetryCount++;
                entity.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(ct);

                _logger.LogWarning("Message {MessageId} marked as failed (attempt {Attempt}): {Error}",
                    messageId, entity.RetryCount, error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking message {MessageId} as failed", messageId);
        }
    }

    /// <inheritdoc />
    public async Task MarkRetryableAsync(string messageId, string error, DateTime nextRetryAt, CancellationToken ct = default)
    {
        try
        {
            // Mark as retryable in in-memory queue
            await _inMemoryQueue.MarkRetryableAsync(messageId, error, nextRetryAt, ct);

            // Update database
            var entity = await _dbContext.QueuedMessages
                .FirstOrDefaultAsync(m => m.Id == messageId, ct);

            if (entity != null)
            {
                entity.LastError = error;
                entity.NextRetryAt = nextRetryAt;
                entity.RetryCount++;
                entity.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Message {MessageId} scheduled for retry at {RetryAt} (attempt {Attempt}): {Error}",
                    messageId, nextRetryAt, entity.RetryCount, error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking message {MessageId} as retryable", messageId);
        }
    }

    /// <inheritdoc />
    public async Task<MessageQueueStats> GetStatsAsync(CancellationToken ct = default)
    {
        try
        {
            var stats = await _inMemoryQueue.GetStatsAsync(ct);

            // Augment with database stats
            var dbStats = await _dbContext.QueuedMessages
                .GroupBy(m => true)
                .Select(g => new
                {
                    Total = g.Count(),
                    Pending = g.Count(m => !m.IsDelivered),
                    Delivered = g.Count(m => m.IsDelivered),
                    Urgent = g.Count(m => m.IsUrgent && !m.IsDelivered),
                    OldestEnqueued = g.Where(m => !m.IsDelivered).Min(m => m.EnqueuedAt),
                    OldestDelivered = g.Where(m => m.IsDelivered).Min(m => m.DeliveredAt)
                })
                .FirstOrDefaultAsync(ct);

            if (dbStats != null)
            {
                _logger.LogDebug("Queue stats - Total DB: {Total}, Pending: {Pending}, Delivered: {Delivered}, Urgent: {Urgent}",
                    dbStats.Total, dbStats.Pending, dbStats.Delivered, dbStats.Urgent);
            }

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving queue stats");
            return new MessageQueueStats();
        }
    }

    /// <summary>
    /// Recovers undelivered messages from database storage on startup.
    /// </summary>
    /// <param name="ct">A token used to cancel the recovery operation.</param>
    /// <returns>A task that completes when recovery has been attempted.</returns>
    public async Task RecoverUndeliveredMessagesAsync(CancellationToken ct = default)
    {
        try
        {
            var undelivered = await _dbContext.QueuedMessages
                .Where(m => !m.IsDelivered)
                .OrderBy(m => m.Priority)
                .ThenBy(m => m.EnqueuedAt)
                .ToListAsync(ct);

            _logger.LogInformation("Recovering {Count} undelivered messages from database", undelivered.Count);

            foreach (var entity in undelivered)
            {
                var message = new QueuedMessage
                {
                    Id = entity.Id,
                    Channel = entity.Channel,
                    Recipient = entity.Recipient,
                    Content = entity.Content,
                    EnqueuedAt = entity.EnqueuedAt,
                    ScheduledFor = entity.ScheduledFor,
                    IsUrgent = entity.IsUrgent,
                    Priority = entity.Priority,
                    IsDelivered = entity.IsDelivered,
                    DeliveredAt = entity.DeliveredAt,
                    RetryAttempts = entity.RetryCount,
                    NextRetryAt = entity.NextRetryAt,
                    LastError = entity.LastError
                };

                await _inMemoryQueue.EnqueueAsync(message, entity.IsUrgent, ct);
            }

            _logger.LogInformation("Message recovery complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recovering undelivered messages");
        }
    }

    /// <summary>
    /// Archives delivered messages by removing records older than the retention window.
    /// </summary>
    /// <param name="daysToKeep">The number of days of delivered messages to retain.</param>
    /// <param name="ct">A token used to cancel the cleanup operation.</param>
    /// <returns>The number of deleted database rows.</returns>
    public async Task<int> CleanupDeliveredMessagesAsync(int daysToKeep = 30, CancellationToken ct = default)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
            var toDelete = await _dbContext.QueuedMessages
                .Where(m => m.IsDelivered && m.DeliveredAt < cutoffDate)
                .ToListAsync(ct);

            _dbContext.QueuedMessages.RemoveRange(toDelete);
            var deleted = await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation("Cleaned up {Count} old delivered messages", deleted);
            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up delivered messages");
            return 0;
        }
    }
}
