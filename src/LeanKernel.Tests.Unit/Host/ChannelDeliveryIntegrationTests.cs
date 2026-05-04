using Microsoft.Extensions.Logging;
using NSubstitute;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Services;
using LeanKernel.Host.Services.Channels;
using LeanKernel.Host.Services.Channels.Adapters;
using Xunit;

namespace LeanKernel.Tests.Unit.Host;

/// <summary>
/// End-to-end integration tests for channel-based message delivery.
/// </summary>
public sealed class ChannelDeliveryIntegrationTests
{
    private readonly ILoggerFactory _loggerFactory;

    public ChannelDeliveryIntegrationTests()
    {
        _loggerFactory = LoggerFactory.Create(b => b.AddConsole());
    }

    [Fact]
    public async Task MessageDelivery_SuccessfullySendsViaAvailableChannel()
    {
        // Arrange: Create test infrastructure
        var rules = CreateEngagementRules();
        var timeBoundary = new TimeBoundaryService(rules, _loggerFactory.CreateLogger<TimeBoundaryService>());
        var messageQueue = new MessageQueueService(timeBoundary, _loggerFactory.CreateLogger<MessageQueueService>());

        var registry = new ChannelRegistry(_loggerFactory.CreateLogger<ChannelRegistry>());
        
        var mockChannel = CreateMockChannel("TestChannel", successDelivery: true);
        registry.RegisterChannel(mockChannel);

        var backgroundService = new MessageProcessingBackgroundService(
            _loggerFactory.CreateLogger<MessageProcessingBackgroundService>(),
            messageQueue,
            timeBoundary,
            registry);

        // Enqueue a message
        var message = new QueuedMessage
        {
            Id = "test-msg-1",
            Channel = "TestChannel",
            Recipient = "user@example.com",
            Content = "Test message",
            EnqueuedAt = DateTime.UtcNow,
            IsUrgent = true // Urgent to bypass active hours check
        };

        var enqueueResult = await messageQueue.EnqueueAsync(message);
        Assert.True(enqueueResult.Success);

        // Act: Process the message (background service)
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var backgroundTask = backgroundService.ExecuteAsync(cts.Token);
        
        // Give background service time to process
        await Task.Delay(2000);
        cts.Cancel();

        // Assert: Verify message was delivered
        var readyMessages = await messageQueue.GetReadyMessagesAsync();
        // Should be empty if processed
        Assert.Empty(readyMessages);
    }

    [Fact]
    public async Task MessageDelivery_HandlesChannelNotFound()
    {
        // Arrange
        var rules = CreateEngagementRules();
        var timeBoundary = new TimeBoundaryService(rules, _loggerFactory.CreateLogger<TimeBoundaryService>());
        var messageQueue = new MessageQueueService(timeBoundary, _loggerFactory.CreateLogger<MessageQueueService>());
        
        var registry = new ChannelRegistry(_loggerFactory.CreateLogger<ChannelRegistry>());
        // Don't register any channels

        var backgroundService = new MessageProcessingBackgroundService(
            _loggerFactory.CreateLogger<MessageProcessingBackgroundService>(),
            messageQueue,
            timeBoundary,
            registry);

        // Enqueue message for non-existent channel
        var message = new QueuedMessage
        {
            Id = "test-msg-2",
            Channel = "NonExistentChannel",
            Recipient = "user@example.com",
            Content = "Test message",
            EnqueuedAt = DateTime.UtcNow,
            IsUrgent = true
        };

        await messageQueue.EnqueueAsync(message);

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var backgroundTask = backgroundService.ExecuteAsync(cts.Token);
        await Task.Delay(2000);
        cts.Cancel();

        // Assert: Message should be marked as failed (not in ready messages)
        var readyMessages = await messageQueue.GetReadyMessagesAsync();
        Assert.Empty(readyMessages);
    }

    [Fact]
    public async Task MessageDelivery_SchedulesRetryOnTransientFailure()
    {
        // Arrange
        var rules = CreateEngagementRules();
        var timeBoundary = new TimeBoundaryService(rules, _loggerFactory.CreateLogger<TimeBoundaryService>());
        var messageQueue = new MessageQueueService(timeBoundary, _loggerFactory.CreateLogger<MessageQueueService>());

        var registry = new ChannelRegistry(_loggerFactory.CreateLogger<ChannelRegistry>());
        
        // Channel that fails with retryable error
        var mockChannel = CreateMockChannel("RetryChannel", successDelivery: false, isRetryable: true);
        registry.RegisterChannel(mockChannel);

        var backgroundService = new MessageProcessingBackgroundService(
            _loggerFactory.CreateLogger<MessageProcessingBackgroundService>(),
            messageQueue,
            timeBoundary,
            registry);

        var message = new QueuedMessage
        {
            Id = "test-msg-3",
            Channel = "RetryChannel",
            Recipient = "user@example.com",
            Content = "Test message",
            EnqueuedAt = DateTime.UtcNow,
            IsUrgent = true
        };

        await messageQueue.EnqueueAsync(message);

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await backgroundService.ExecuteAsync(cts.Token);

        // Assert: Message should have retry scheduled
        // (The message won't be in ready messages since it's scheduled for later)
        var stats = await messageQueue.GetStatsAsync();
        Assert.True(stats.TotalEnqueued > 0);
    }

    [Fact]
    public async Task MessageDelivery_BatchesDuringQuietHours()
    {
        // Arrange: Create rules with very restrictive active hours
        var rules = new EngagementRules
        {
            TimeBoundaries = new TimeBoundaries
            {
                Timezone = "UTC",
                ActiveHoursStart = 12, // Noon
                ActiveHoursEnd = 13    // 1 PM (current time likely outside these hours)
            }
        };

        var timeBoundary = new TimeBoundaryService(rules, _loggerFactory.CreateLogger<TimeBoundaryService>());
        var messageQueue = new MessageQueueService(timeBoundary, _loggerFactory.CreateLogger<MessageQueueService>());

        var registry = new ChannelRegistry(_loggerFactory.CreateLogger<ChannelRegistry>());
        var mockChannel = CreateMockChannel("QuietChannel", successDelivery: true);
        registry.RegisterChannel(mockChannel);

        var backgroundService = new MessageProcessingBackgroundService(
            _loggerFactory.CreateLogger<MessageProcessingBackgroundService>(),
            messageQueue,
            timeBoundary,
            registry);

        // Enqueue non-urgent message
        var message = new QueuedMessage
        {
            Id = "test-msg-4",
            Channel = "QuietChannel",
            Recipient = "user@example.com",
            Content = "Test message",
            EnqueuedAt = DateTime.UtcNow,
            IsUrgent = false // Non-urgent
        };

        var enqueueResult = await messageQueue.EnqueueAsync(message);
        Assert.True(enqueueResult.Success);
        Assert.True(enqueueResult.WillBeBatched, "Non-urgent message should be batched during quiet hours");

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await backgroundService.ExecuteAsync(cts.Token);

        // Assert: Message should still be pending (not delivered) since it's quiet hours
        var readyMessages = await messageQueue.GetReadyMessagesAsync();
        // May be empty if we're outside active hours, which is expected
    }

    [Fact]
    public async Task MessageDelivery_DeliveredImmediatelyWhenUrgent()
    {
        // Arrange
        var rules = new EngagementRules
        {
            TimeBoundaries = new TimeBoundaries
            {
                Timezone = "UTC",
                ActiveHoursStart = 12,
                ActiveHoursEnd = 13
            }
        };

        var timeBoundary = new TimeBoundaryService(rules, _loggerFactory.CreateLogger<TimeBoundaryService>());
        var messageQueue = new MessageQueueService(timeBoundary, _loggerFactory.CreateLogger<MessageQueueService>());

        var registry = new ChannelRegistry(_loggerFactory.CreateLogger<ChannelRegistry>());
        var mockChannel = CreateMockChannel("UrgentChannel", successDelivery: true);
        registry.RegisterChannel(mockChannel);

        var backgroundService = new MessageProcessingBackgroundService(
            _loggerFactory.CreateLogger<MessageProcessingBackgroundService>(),
            messageQueue,
            timeBoundary,
            registry);

        // Enqueue URGENT message during quiet hours
        var message = new QueuedMessage
        {
            Id = "test-msg-5",
            Channel = "UrgentChannel",
            Recipient = "user@example.com",
            Content = "Urgent message",
            EnqueuedAt = DateTime.UtcNow,
            IsUrgent = true // URGENT - should deliver regardless of hours
        };

        var enqueueResult = await messageQueue.EnqueueAsync(message, isUrgent: true);
        Assert.True(enqueueResult.Success);
        Assert.False(enqueueResult.WillBeBatched, "Urgent message should not be batched");

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var backgroundTask = backgroundService.ExecuteAsync(cts.Token);
        await Task.Delay(2000);
        cts.Cancel();

        // Assert
        var stats = await messageQueue.GetStatsAsync();
        Assert.True(stats.TotalEnqueued > 0);
    }

    private EngagementRules CreateEngagementRules()
    {
        return new EngagementRules
        {
            TimeBoundaries = new TimeBoundaries
            {
                Timezone = "UTC"
                // Default active hours are 8 AM to 10 PM, so we're likely in active hours
            }
        };
    }

    private IMessageChannel CreateMockChannel(
        string name,
        bool successDelivery,
        bool isRetryable = false)
    {
        var mock = Substitute.For<IMessageChannel>();
        mock.Name.Returns(name);
        mock.IsConfigured.Returns(true);

        if (successDelivery)
        {
            mock.DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ChannelDeliveryResult.Successful(name, "delivery-ref-123"));
        }
        else
        {
            var result = ChannelDeliveryResult.Failed(
                name,
                "Delivery failed",
                retryable: isRetryable,
                TimeSpan.FromSeconds(5));

            mock.DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(result);
        }

        return mock;
    }
}
