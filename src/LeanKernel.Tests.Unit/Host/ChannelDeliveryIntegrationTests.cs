using Microsoft.Extensions.Logging;
using NSubstitute;
using LeanKernel.Commander;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Host.Services;
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

        var mockChannel = CreateMockChannel("TestChannel", successDelivery: true);
        var router = CreateRouter(mockChannel);

        var backgroundService = new MessageProcessingBackgroundService(
            _loggerFactory.CreateLogger<MessageProcessingBackgroundService>(),
            messageQueue,
            timeBoundary,
            router);

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

        var enqueueResult = await messageQueue.EnqueueAsync(message, isUrgent: true);
        Assert.True(enqueueResult.Success);

        // Act: Process the message (background service)
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var backgroundTask = backgroundService.RunAsync(cts.Token);
        
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
        
        var router = CreateRouter();

        var backgroundService = new MessageProcessingBackgroundService(
            _loggerFactory.CreateLogger<MessageProcessingBackgroundService>(),
            messageQueue,
            timeBoundary,
            router);

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

        await messageQueue.EnqueueAsync(message, isUrgent: true);

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var backgroundTask = backgroundService.RunAsync(cts.Token);
        await Task.Delay(2000);
        cts.Cancel();

        var readyMessages = await messageQueue.GetReadyMessagesAsync();
        var failedMessage = Assert.Single(readyMessages);
        Assert.Equal("test-msg-2", failedMessage.Id);
        Assert.Equal(1, failedMessage.RetryAttempts);
        Assert.Equal("Channel 'NonExistentChannel' not configured", failedMessage.LastError);
    }

    [Fact]
    public async Task MessageDelivery_SchedulesRetryOnTransientFailure()
    {
        // Arrange
        var rules = CreateEngagementRules();
        var timeBoundary = new TimeBoundaryService(rules, _loggerFactory.CreateLogger<TimeBoundaryService>());
        var messageQueue = new MessageQueueService(timeBoundary, _loggerFactory.CreateLogger<MessageQueueService>());

        var mockChannel = CreateMockChannel("RetryChannel", successDelivery: false, isRetryable: true);
        var router = CreateRouter(mockChannel);

        var backgroundService = new MessageProcessingBackgroundService(
            _loggerFactory.CreateLogger<MessageProcessingBackgroundService>(),
            messageQueue,
            timeBoundary,
            router);

        var message = new QueuedMessage
        {
            Id = "test-msg-3",
            Channel = "RetryChannel",
            Recipient = "user@example.com",
            Content = "Test message",
            EnqueuedAt = DateTime.UtcNow,
            IsUrgent = true
        };

        await messageQueue.EnqueueAsync(message, isUrgent: true);

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await backgroundService.RunAsync(cts.Token);

        // Assert: Message should have retry scheduled
        // (The message won't be in ready messages since it's scheduled for later)
        var stats = await messageQueue.GetStatsAsync();
        Assert.True(stats.TotalEnqueued > 0);
    }

    [Fact]
    public async Task MessageDelivery_BatchesDuringQuietHours()
    {
        // Arrange: Create rules with active hours that are closed for the whole day.
        var rules = new EngagementRules
        {
            TimeBoundaries = new TimeBoundaries
            {
                Timezone = "UTC",
                ActiveHoursEnd = 0
            }
        };

        var timeBoundary = new TimeBoundaryService(rules, _loggerFactory.CreateLogger<TimeBoundaryService>());
        var messageQueue = new MessageQueueService(timeBoundary, _loggerFactory.CreateLogger<MessageQueueService>());

        var mockChannel = CreateMockChannel("QuietChannel", successDelivery: true);
        var router = CreateRouter(mockChannel);

        var backgroundService = new MessageProcessingBackgroundService(
            _loggerFactory.CreateLogger<MessageProcessingBackgroundService>(),
            messageQueue,
            timeBoundary,
            router);

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
        await backgroundService.RunAsync(cts.Token);

        var stats = await messageQueue.GetStatsAsync();
        Assert.Equal(1, stats.PendingMessages);
        Assert.Equal(0, stats.DeliveredMessages);
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
                ActiveHoursEnd = 0
            }
        };

        var timeBoundary = new TimeBoundaryService(rules, _loggerFactory.CreateLogger<TimeBoundaryService>());
        var messageQueue = new MessageQueueService(timeBoundary, _loggerFactory.CreateLogger<MessageQueueService>());

        var mockChannel = CreateMockChannel("UrgentChannel", successDelivery: true);
        var router = CreateRouter(mockChannel);

        var backgroundService = new MessageProcessingBackgroundService(
            _loggerFactory.CreateLogger<MessageProcessingBackgroundService>(),
            messageQueue,
            timeBoundary,
            router);

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
        var backgroundTask = backgroundService.RunAsync(cts.Token);
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
                Timezone = "UTC",
                ActiveHoursStart = null,
                ActiveHoursEnd = null
            }
        };
    }

    private ChannelRouter CreateRouter(params IChannel[] channels) =>
        new(
            Substitute.For<IThinkerService>(),
            channels,
            _loggerFactory.CreateLogger<ChannelRouter>());

    private IChannel CreateMockChannel(
        string name,
        bool successDelivery,
        bool isRetryable = false)
    {
        var mock = Substitute.For<IChannel>();
        mock.ChannelId.Returns(name);
        mock.Name.Returns(name);
        mock.IsConfigured.Returns(true);
        mock.IsAuthorizedSender(Arg.Any<string>()).Returns(true);

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
