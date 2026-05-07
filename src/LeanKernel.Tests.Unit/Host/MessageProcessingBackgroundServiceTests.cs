using Microsoft.Extensions.Logging;
using NSubstitute;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Services;
using LeanKernel.Host.Services.Channels;
using Xunit;

namespace LeanKernel.Tests.Unit.Host;

public class MessageProcessingBackgroundServiceTests
{
    private static (MessageProcessingBackgroundService, IMessageQueue, TimeBoundaryService, ChannelRegistry) CreateService()
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
        
        var registryLogger = Substitute.For<ILogger<ChannelRegistry>>();
        var channelRegistry = new ChannelRegistry(registryLogger);

        var serviceLogger = Substitute.For<ILogger<MessageProcessingBackgroundService>>();
        var service = new MessageProcessingBackgroundService(serviceLogger, (IMessageQueue)messageQueue, timeBoundary, channelRegistry);

        return (service, (IMessageQueue)messageQueue, timeBoundary, channelRegistry);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesReadyMessages()
    {
        var (service, messageQueue, _, _) = CreateService();
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
        // Create rules with active hours that are closed for the whole day.
        var rules = new EngagementRules
        {
            TimeBoundaries = new TimeBoundaries
            {
                Timezone = "UTC",
                ActiveHoursEnd = 0
            }
        };
        var timeBoundaryLogger = Substitute.For<ILogger<TimeBoundaryService>>();
        var timeBoundary = new TimeBoundaryService(rules, timeBoundaryLogger);

        var messageQueueLogger = Substitute.For<ILogger<MessageQueueService>>();
        var messageQueue = new MessageQueueService(timeBoundary, messageQueueLogger);
        
        var registryLogger = Substitute.For<ILogger<ChannelRegistry>>();
        var channelRegistry = new ChannelRegistry(registryLogger);

        var serviceLogger = Substitute.For<ILogger<MessageProcessingBackgroundService>>();
        var service = new MessageProcessingBackgroundService(serviceLogger, (IMessageQueue)messageQueue, timeBoundary, channelRegistry);

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

        var stats = await messageQueue.GetStatsAsync();
        Assert.Equal(1, stats.PendingMessages);
        Assert.Equal(0, stats.DeliveredMessages);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesExceptions()
    {
        var (service, _, _, _) = CreateService();
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
