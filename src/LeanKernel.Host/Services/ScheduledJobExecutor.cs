using LeanKernel.Commander;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Host.Services;

/// <summary>
/// Executes scheduled proactive jobs through Thinker and Commander delivery.
/// </summary>
public sealed class ScheduledJobExecutor : IProactiveJobExecutor
{
    private readonly IThinkerService _thinker;
    private readonly ChannelRouter _channelRouter;
    private readonly ILogger<ScheduledJobExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduledJobExecutor" /> class.
    /// </summary>
    public ScheduledJobExecutor(
        IThinkerService thinker,
        ChannelRouter channelRouter,
        ILogger<ScheduledJobExecutor> logger)
    {
        _thinker = thinker;
        _channelRouter = channelRouter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ScheduledJobExecutionResult> ExecuteAsync(ScheduledJobDefinition job, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.DeliveryChannel) || string.IsNullOrWhiteSpace(job.DeliveryRecipient))
        {
            return ScheduledJobExecutionResult.Failed(
                "Scheduled job is missing delivery channel or recipient.",
                reason: "missing_delivery_target",
                deliveryStatus: "invalid");
        }

        try
        {
            var inboundMessage = new LeanKernelMessage
            {
                Id = $"scheduled-{job.Id}-{Guid.NewGuid():N}",
                ChannelId = job.DeliveryChannel,
                SenderId = job.DeliveryRecipient,
                Content = job.PayloadMessage,
                Timestamp = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["scheduled_job_id"] = job.Id,
                    ["scheduled_job_name"] = job.Name,
                    ["scheduled_job_scope"] = job.Scope.ToString().ToLowerInvariant(),
                    ["is_admin"] = job.Scope == ScheduledJobScope.Global ? "true" : "false"
                }
            };

            var response = await _thinker.ProcessAsync(inboundMessage, ct);
            if (string.IsNullOrWhiteSpace(response))
                response = "Scheduled job executed successfully.";

            var delivery = await _channelRouter.DeliverAsync(
                job.DeliveryChannel,
                job.DeliveryRecipient,
                response,
                ct);

            if (!delivery.Success)
            {
                _logger.LogWarning(
                    "Scheduled job {JobId} delivery failed: {Error}",
                    job.Id,
                    delivery.Error ?? "unknown");
                return ScheduledJobExecutionResult.Failed(
                    delivery.Error ?? "Delivery failed.",
                    reason: delivery.IsRetryable ? "delivery_retryable" : "delivery_error",
                    deliveryStatus: "failed");
            }

            return ScheduledJobExecutionResult.Successful(
                deliveryStatus: "delivered",
                deliveryReference: delivery.DeliveryReference);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled job {JobId} execution failed", job.Id);
            return ScheduledJobExecutionResult.Failed(ex.Message, reason: "execution_exception", deliveryStatus: "error");
        }
    }
}
