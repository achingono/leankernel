using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LeanKernel.Host.Data;

namespace LeanKernel.Host.Services;

/// <summary>
/// Message queue service with persistent database storage.
/// Wraps the in-memory queue and syncs to SQLite for durability.
/// </summary>
public class PersistentMessageQueueService : IMessageQueue
{
    private readonly IMessageQueue _inMemoryQueue;
    private readonly MessageQueueDbContext _dbContext;
    private readonly ILogger<PersistentMessageQueueService> _logger;

    public PersistentMessageQueueService(
        IMessageQueue inMemoryQueue,
        MessageQueueDbContext dbContext,
        ILogger<PersistentMessageQueueService> logger)
    {
        _inMemoryQueue = inMemoryQueue;
        _dbContext = dbContext;
        _logger = logger;
    }

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
    /// Recover undelivered messages from database on startup.
    /// </summary>
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
                    DeliveredAt = entity.DeliveredAt
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
    /// Archive delivered messages and clean up old records.
    /// </summary>
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
