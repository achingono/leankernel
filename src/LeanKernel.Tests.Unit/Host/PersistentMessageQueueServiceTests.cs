using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using LeanKernel.Host.Services;

namespace LeanKernel.Tests.Unit.Host;

public class PersistentMessageQueueServiceTests
{
    private static MessageQueueDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<MessageQueueDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new MessageQueueDbContext(options);
    }

    private static (IMessageQueue inMemory, PersistentMessageQueueService persistent, MessageQueueDbContext db) CreateServices()
    {
        var inMemoryQueue = new MessageQueueService(
            Substitute.For<ITimeBoundaryService>(),
            Substitute.For<ILogger<MessageQueueService>>());

        var db = CreateInMemoryDbContext();
        db.Database.EnsureCreated();

        var persistentQueue = new PersistentMessageQueueService(
            inMemoryQueue,
            db,
            Substitute.For<ILogger<PersistentMessageQueueService>>());

        return (inMemoryQueue, persistentQueue, db);
    }

    [Fact]
    public async Task EnqueueAsync_PersistsMessageToDatabase()
    {
        // Arrange
        var (_, persistent, db) = CreateServices();
        var message = new QueuedMessage
        {
            Id = "test-1",
            Channel = "console",
            Recipient = "user@example.com",
            Content = "Test message",
            EnqueuedAt = DateTime.UtcNow
        };

        // Act
        await persistent.EnqueueAsync(message, false, CancellationToken.None);

        // Assert
        var stored = await db.QueuedMessages.FirstOrDefaultAsync(m => m.Id == "test-1");
        Assert.NotNull(stored);
        Assert.Equal("console", stored.Channel);
        Assert.Equal("Test message", stored.Content);
        Assert.False(stored.IsDelivered);
    }

    [Fact]
    public async Task EnqueueAsync_MultiplMessages_AllPersisted()
    {
        // Arrange
        var (_, persistent, db) = CreateServices();

        // Act
        for (int i = 1; i <= 3; i++)
        {
            var message = new QueuedMessage
            {
                Id = $"msg-{i}",
                Channel = "console",
                Recipient = "user@example.com",
                Content = $"Message {i}",
                EnqueuedAt = DateTime.UtcNow
            };
            await persistent.EnqueueAsync(message, false, CancellationToken.None);
        }

        // Assert
        var count = await db.QueuedMessages.CountAsync();
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task MarkDeliveredAsync_UpdatesDatabaseStatus()
    {
        // Arrange
        var (_, persistent, db) = CreateServices();
        var message = new QueuedMessage
        {
            Id = "test-1",
            Channel = "console",
            Recipient = "user@example.com",
            Content = "Test",
            EnqueuedAt = DateTime.UtcNow
        };

        await persistent.EnqueueAsync(message, false, CancellationToken.None);

        // Act
        await persistent.MarkDeliveredAsync("test-1", CancellationToken.None);

        // Assert
        var stored = await db.QueuedMessages.FirstOrDefaultAsync(m => m.Id == "test-1");
        Assert.NotNull(stored);
        Assert.True(stored.IsDelivered);
        Assert.NotNull(stored.DeliveredAt);
    }

    [Fact]
    public async Task RecoverUndeliveredMessagesAsync_RestoresUndeliveredMessages()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        db.Database.EnsureCreated();

        // Manually add undelivered messages to database
        db.QueuedMessages.AddRange(
            new QueuedMessageEntity
            {
                Id = "msg-1",
                Channel = "console",
                Recipient = "user@example.com",
                Content = "Undelivered 1",
                EnqueuedAt = DateTime.UtcNow,
                IsDelivered = false,
                Priority = 5,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new QueuedMessageEntity
            {
                Id = "msg-2",
                Channel = "console",
                Recipient = "user@example.com",
                Content = "Undelivered 2",
                EnqueuedAt = DateTime.UtcNow,
                IsDelivered = false,
                Priority = 5,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var inMemoryQueue = new MessageQueueService(
            Substitute.For<ITimeBoundaryService>(),
            Substitute.For<ILogger<MessageQueueService>>());

        var persistent = new PersistentMessageQueueService(
            inMemoryQueue,
            db,
            Substitute.For<ILogger<PersistentMessageQueueService>>());

        // Act
        await persistent.RecoverUndeliveredMessagesAsync(CancellationToken.None);

        // Assert
        var ready = await persistent.GetReadyMessagesAsync(CancellationToken.None);
        Assert.NotEmpty(ready);
        Assert.Contains(ready, m => m.Id == "msg-1");
        Assert.Contains(ready, m => m.Id == "msg-2");
    }

    [Fact]
    public async Task CleanupDeliveredMessagesAsync_RemovesOldDeliveredMessages()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        db.Database.EnsureCreated();

        var oldDate = DateTime.UtcNow.AddDays(-40);
        var recentDate = DateTime.UtcNow.AddDays(-5);

        db.QueuedMessages.AddRange(
            new QueuedMessageEntity
            {
                Id = "old-1",
                Channel = "console",
                Recipient = "user@example.com",
                Content = "Old delivered",
                EnqueuedAt = oldDate,
                IsDelivered = true,
                DeliveredAt = oldDate,
                Priority = 5,
                CreatedAt = oldDate,
                UpdatedAt = oldDate
            },
            new QueuedMessageEntity
            {
                Id = "recent-1",
                Channel = "console",
                Recipient = "user@example.com",
                Content = "Recent delivered",
                EnqueuedAt = recentDate,
                IsDelivered = true,
                DeliveredAt = recentDate,
                Priority = 5,
                CreatedAt = recentDate,
                UpdatedAt = recentDate
            });
        await db.SaveChangesAsync();

        var persistent = new PersistentMessageQueueService(
            Substitute.For<IMessageQueue>(),
            db,
            Substitute.For<ILogger<PersistentMessageQueueService>>());

        // Act
        var deleted = await persistent.CleanupDeliveredMessagesAsync(daysToKeep: 30, CancellationToken.None);

        // Assert
        Assert.Equal(1, deleted); // Only old-1 should be deleted
        var remaining = await db.QueuedMessages.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("recent-1", remaining.First().Id);
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsQueueStatistics()
    {
        // Arrange
        var (inMemory, persistent, db) = CreateServices();
        var msg1 = new QueuedMessage
        {
            Id = "msg-1",
            Channel = "console",
            Recipient = "user@example.com",
            Content = "Pending",
            EnqueuedAt = DateTime.UtcNow
        };
        var msg2 = new QueuedMessage
        {
            Id = "msg-2",
            Channel = "console",
            Recipient = "user@example.com",
            Content = "Urgent",
            EnqueuedAt = DateTime.UtcNow,
            IsUrgent = true
        };

        await persistent.EnqueueAsync(msg1, false, CancellationToken.None);
        await persistent.EnqueueAsync(msg2, true, CancellationToken.None);

        // Act
        var stats = await persistent.GetStatsAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(2, stats.PendingMessages);
        Assert.Equal(1, stats.UrgentMessages);
    }

    [Fact]
    public async Task EnqueueAsync_WithUrgentFlag_SetsIsUrgentInDatabase()
    {
        // Arrange
        var (_, persistent, db) = CreateServices();
        var message = new QueuedMessage
        {
            Id = "test-1",
            Channel = "console",
            Recipient = "user@example.com",
            Content = "Important",
            EnqueuedAt = DateTime.UtcNow,
            IsUrgent = false
        };

        // Act
        await persistent.EnqueueAsync(message, isUrgent: true, CancellationToken.None);

        // Assert
        var stored = await db.QueuedMessages.FirstOrDefaultAsync(m => m.Id == "test-1");
        Assert.NotNull(stored);
        Assert.True(stored.IsUrgent); // Should be marked urgent due to flag override
    }
}
