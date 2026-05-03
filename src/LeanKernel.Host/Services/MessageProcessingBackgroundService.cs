using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Host.Services;

/// <summary>
/// Background service that processes queued messages at regular intervals,
/// especially at the start of active hours.
/// </summary>
public sealed class MessageProcessingBackgroundService : BackgroundService
{
    private readonly ILogger<MessageProcessingBackgroundService> _logger;
    private readonly MessageQueueService _messageQueue;
    private readonly TimeBoundaryService _timeBoundary;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    public MessageProcessingBackgroundService(
        ILogger<MessageProcessingBackgroundService> logger,
        MessageQueueService messageQueue,
        TimeBoundaryService timeBoundary)
    {
        _logger = logger;
        _messageQueue = messageQueue;
        _timeBoundary = timeBoundary;
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
                // Here we would typically send the message to the appropriate channel
                // For now, just log it and mark as delivered
                _logger.LogInformation(
                    "Delivering queued message {MessageId} to {Channel} (priority: {Priority})",
                    message.Id,
                    message.Channel,
                    message.Priority);

                // Mark as delivered
                await _messageQueue.MarkDeliveredAsync(message.Id, ct);
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
}
