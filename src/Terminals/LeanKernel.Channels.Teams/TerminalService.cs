using LeanKernel.Channels.Teams.Clients;

namespace LeanKernel.Channels.Teams;

/// <summary>Background service that polls for Teams activities and forwards them to the LeanKernel gateway.</summary>
public sealed class TerminalService(
    ILogger<TerminalService> logger,
    ITransportClient transport,
    GatewayClient gatewayClient) : BackgroundService
{
    /// <summary>Executes the background loop for processing Teams activities.</summary>
    /// <param name="stoppingToken">Cancellation token signaled when the service is shutting down.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var activity = await transport.ReceiveAsync(stoppingToken);
                if (activity is null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(activity.BearerToken))
                {
                    logger.LogWarning("Rejecting Teams sender {SenderId}; no provisioned credential is available.", activity.SenderId);
                    continue;
                }

                var input = AttachmentParser.BuildGatewayInput(activity.Text, activity.Attachments);
                var result = await gatewayClient.RunTurnAsync(input, activity.BearerToken, activity.Attachments, stoppingToken);
                if (activity.Attachments.Count > 0)
                {
                    result = $"{result}\n\n(attachments={activity.Attachments.Count})";
                }

                await transport.SendAsync(activity, result, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in Teams terminal loop; restarting in 5 seconds");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
