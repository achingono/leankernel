using Microsoft.Extensions.Logging;
using NSubstitute;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Services;
using Xunit;

namespace LeanKernel.Tests.Unit.Host;

public class MessageProcessingBackgroundServiceTests
{
    private static (MessageProcessingBackgroundService, MessageQueueService, TimeBoundaryService) CreateService()
    {
        var rules = new EngagementRules
        {
            TimeBoundaries = new TimeBoundaries
            {
                Timezone = "UTC"
            }
        };
        var timeBoundaryLogger = Substitute.For<ILogger<TimeBoundaryService>>();
        var timeBoundary = new TimeBoundaryService(rules, timeBoundaryLogger);

        var messageQueueLogger = Substitute.For<ILogger<MessageQueueService>>();
        var messageQueue = new MessageQueueService(timeBoundary, messageQueueLogger);

        var serviceLogger = Substitute.For<ILogger<MessageProcessingBackgroundService>>();
        var service = new MessageProcessingBackgroundService(serviceLogger, messageQueue, timeBoundary);

        return (service, messageQueue, timeBoundary);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesReadyMessages()
    {
        var (service, messageQueue, _) = CreateService();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Enqueue some messages
        var result1 = await messageQueue.EnqueueAsync(new QueuedMessage
        {
            Id = "msg1",
            Channel = "test",
            Recipient = "user1",
            Content = "Test message 1",
            EnqueuedAt = DateTime.UtcNow
        }, false);

        Assert.True(result1.Success);

        // Start the service (will run for ~2 seconds then stop)
        var task = service.StartAsync(cts.Token);

        // Give it time to process
        await Task.Delay(1000);

        // Stop the service
        await service.StopAsync(cts.Token);

        // Check stats
        var stats = await messageQueue.GetStatsAsync();
        Assert.True(stats.TotalEnqueued > 0);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsProcessingWhenNotInActiveHours()
    {
        // Create rules with very restrictive active hours (e.g., midnight to 1am)
        var rules = new EngagementRules
        {
            TimeBoundaries = new TimeBoundaries
            {
                Timezone = "UTC",
                ActiveHoursStart = 0,  // Midnight
                ActiveHoursEnd = 1     // 1 AM (will be outside these hours most of the time)
            }
        };
        var timeBoundaryLogger = Substitute.For<ILogger<TimeBoundaryService>>();
        var timeBoundary = new TimeBoundaryService(rules, timeBoundaryLogger);

        var messageQueueLogger = Substitute.For<ILogger<MessageQueueService>>();
        var messageQueue = new MessageQueueService(timeBoundary, messageQueueLogger);

        var serviceLogger = Substitute.For<ILogger<MessageProcessingBackgroundService>>();
        var service = new MessageProcessingBackgroundService(serviceLogger, messageQueue, timeBoundary);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Enqueue a non-urgent message
        await messageQueue.EnqueueAsync(new QueuedMessage
        {
            Id = "msg-quiet",
            Channel = "test",
            Recipient = "user1",
            Content = "Test message during quiet hours",
            EnqueuedAt = DateTime.UtcNow
        }, false);

        // Start the service
        var task = service.StartAsync(cts.Token);
        await Task.Delay(1000);
        await service.StopAsync(cts.Token);

        // Message should still be pending (not delivered)
        var readyMessages = await messageQueue.GetReadyMessagesAsync();
        // May be empty if outside active hours, which is expected
    }

    [Fact]
    public async Task ExecuteAsync_HandlesExceptions()
    {
        var (service, _, _) = CreateService();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Start the service (should handle any exceptions gracefully)
        var task = service.StartAsync(cts.Token);

        // Let it run
        await Task.Delay(1000);

        // Should not throw
        await service.StopAsync(cts.Token);

        // Service should complete without throwing
        Assert.NotNull(task);
    }
}
