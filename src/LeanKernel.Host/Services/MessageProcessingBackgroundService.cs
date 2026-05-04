using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LeanKernel.Host.Services.Channels;

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
    private readonly ChannelRegistry _channelRegistry;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    public MessageProcessingBackgroundService(
        ILogger<MessageProcessingBackgroundService> logger,
        IMessageQueue messageQueue,
        TimeBoundaryService timeBoundary,
        ChannelRegistry channelRegistry)
    {
        _logger = logger;
        _messageQueue = messageQueue;
        _timeBoundary = timeBoundary;
        _channelRegistry = channelRegistry;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
                await Task.Delay(_checkInterval, stoppingToken);
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
        if (!status.IsInActiveHours)
        {
            // Not in active hours, skip processing
            return;
        }

        // Get ready messages (urgent messages and messages scheduled for now)
        var readyMessages = await _messageQueue.GetReadyMessagesAsync(ct);

        if (readyMessages.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Processing {Count} queued messages", readyMessages.Count);

        // Process each message
        foreach (var message in readyMessages)
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
        var channel = _channelRegistry.GetChannel(message.Channel);

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
        var result = await channel.DeliverAsync(message.Recipient, message.Content, ct);

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
