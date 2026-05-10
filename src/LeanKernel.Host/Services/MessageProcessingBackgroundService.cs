using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LeanKernel.Commander;

namespace LeanKernel.Host.Services;

/// <summary>
/// Background service that processes queued messages at regular intervals,
/// especially at the start of active hours. Routes messages through appropriate channels.
/// </summary>
public sealed class MessageProcessingBackgroundService : BackgroundService
{
    private readonly ILogger<MessageProcessingBackgroundService> _logger;
    private readonly IMessageQueue _messageQueue;
    private readonly TimeBoundaryService _timeBoundary;
    private readonly ChannelRouter _channelRouter;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    public MessageProcessingBackgroundService(
        ILogger<MessageProcessingBackgroundService> logger,
        IMessageQueue messageQueue,
        TimeBoundaryService timeBoundary,
        ChannelRouter channelRouter)
    {
        _logger = logger;
        _messageQueue = messageQueue;
        _timeBoundary = timeBoundary;
        _channelRouter = channelRouter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunAsync(stoppingToken);
    }

    /// <summary>
    /// Public entry point for testing. Runs the message processing loop until cancellation.
    /// </summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MessageProcessingBackgroundService started");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessQueuedMessagesAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected when service is stopping
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing queued messages");
                }

                // Wait before checking again
                try
                {
                    await Task.Delay(_checkInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            _logger.LogInformation("MessageProcessingBackgroundService stopped");
        }
    }

    private async Task ProcessQueuedMessagesAsync(CancellationToken ct)
    {
        // Check if we're in active hours
        var status = _timeBoundary.GetStatus();
        var inActiveHours = status.IsInActiveHours;

        // Get ready messages
        var readyMessages = await _messageQueue.GetReadyMessagesAsync(ct);

        if (readyMessages.Count == 0)
        {
            return;
        }

        // Filter messages to process:
        // - In active hours: process all messages
        // - Outside active hours: process only urgent messages (priority 1)
        var messagesToProcess = inActiveHours
            ? readyMessages
            : readyMessages.Where(m => m.Priority == 1).ToList();

        if (messagesToProcess.Count == 0)
        {
            if (readyMessages.Count > 0 && !inActiveHours)
            {
                _logger.LogDebug(
                    "Not in active hours: {PendingCount} non-urgent messages waiting for active window at {NextActive}",
                    readyMessages.Count,
                    status.NextActiveWindow);
            }
            return;
        }

        _logger.LogInformation("Processing {Count} queued messages", messagesToProcess.Count);

        // Process each message
        foreach (var message in messagesToProcess)
        {
            try
            {
                await DeliverMessageAsync(message, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deliver message {MessageId}", message.Id);
            }
        }

        // Log queue statistics
        var stats = await _messageQueue.GetStatsAsync(ct);
        _logger.LogInformation(
            "Queue stats: Total={Total}, Pending={Pending}, Delivered={Delivered}",
            stats.TotalEnqueued,
            stats.PendingMessages,
            stats.DeliveredMessages);
    }

    private async Task DeliverMessageAsync(QueuedMessage message, CancellationToken ct)
    {
        // Resolve the channel adapter
        var channel = _channelRouter.GetChannel(message.Channel);

        if (channel == null)
        {
            _logger.LogError(
                "Channel '{ChannelName}' not available for message {MessageId}",
                message.Channel,
                message.Id);

            await _messageQueue.MarkFailedAsync(
                message.Id,
                $"Channel '{message.Channel}' not configured",
                ct);

            return;
        }

        _logger.LogInformation(
            "Delivering message {MessageId} to {Channel}/{Recipient}",
            message.Id,
            message.Channel,
            message.Recipient);

        // Attempt delivery
        var result = await _channelRouter.DeliverAsync(message.Channel, message.Recipient, message.Content, ct);

        if (result.Success)
        {
            // Mark as delivered
            await _messageQueue.MarkDeliveredAsync(message.Id, ct);

            _logger.LogInformation(
                "Successfully delivered message {MessageId} via {Channel} (ref: {Reference})",
                message.Id,
                message.Channel,
                result.DeliveryReference ?? "N/A");
        }
        else if (result.IsRetryable && result.SuggestedRetryDelay.HasValue)
        {
            // Schedule for retry
            var nextRetryAt = DateTime.UtcNow.Add(result.SuggestedRetryDelay.Value);

            await _messageQueue.MarkRetryableAsync(
                message.Id,
                result.Error ?? "Unknown error",
                nextRetryAt,
                ct);

            _logger.LogWarning(
                "Message {MessageId} failed (retryable), scheduled for retry at {RetryAt}: {Error}",
                message.Id,
                nextRetryAt,
                result.Error ?? "Unknown error");
        }
        else
        {
            // Mark as failed (not retryable)
            await _messageQueue.MarkFailedAsync(
                message.Id,
                result.Error ?? "Unknown error",
                ct);

            _logger.LogError(
                "Message {MessageId} delivery failed (not retryable): {Error}",
                message.Id,
                result.Error ?? "Unknown error");
        }
    }
}
