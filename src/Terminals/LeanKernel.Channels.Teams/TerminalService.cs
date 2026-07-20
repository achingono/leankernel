using Microsoft.Extensions.Options;
using LeanKernel.Channels.Teams.Clients;

namespace LeanKernel.Channels.Teams;

public sealed class TerminalService(
    ILogger<TerminalService> logger,
    ITransportClient transport,
    GatewayClient gatewayClient) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
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

            var attachments = AttachmentParser.Parse(activity.AttachmentUrls);
            var result = await gatewayClient.RunTurnAsync(activity.Text, activity.BearerToken, stoppingToken);
            if (attachments.Count > 0)
            {
                result = $"{result}\n\n(attachments={attachments.Count})";
            }

            await transport.SendAsync(activity, result, stoppingToken);
        }
    }
}
