using Microsoft.Extensions.Options;

namespace LeanKernel.Channels.Signal;

/// <summary>
/// Background service that continuously receives Signal messages, processes them through the gateway agent, and sends responses.
/// </summary>
public sealed class TerminalService(
    ILogger<TerminalService> logger,
    ITransportClient transport,
    GatewayChannelClient gatewayClient,
    IOptions<SignalSettings> signalSettings) : BackgroundService
{
    /// <summary>
    /// Executes the main message processing loop.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token that signals service shutdown.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var inbound = await transport.ReceiveAsync(stoppingToken);
            if (inbound is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                continue;
            }

            if (string.IsNullOrWhiteSpace(inbound.BearerToken))
            {
                logger.LogWarning("Rejecting Signal sender {Sender}; no provisioned credential is available.", inbound.Sender);
                continue;
            }

            try
            {
                var attachmentHints = AttachmentParser.ParseAttachmentHints(inbound.Text);
                var input = AttachmentParser.BuildGatewayInput(inbound.Text, inbound.Attachments, attachmentHints);
                await using var typingKeepAlive = TypingIndicatorKeepAlive.Start(
                    transport,
                    inbound.Account,
                    inbound.Sender,
                    signalSettings.Value,
                    logger,
                    stoppingToken);

                var output = await gatewayClient.RunTurnAsync(input, inbound.BearerToken, stoppingToken);

                var attachmentCount = inbound.Attachments.Count > 0
                    ? inbound.Attachments.Count
                    : attachmentHints.Count;

                if (attachmentCount > 0)
                {
                    output = output with
                    {
                        Text = $"{output.Text}\n\n(attachments={attachmentCount})"
                    };
                }

                await transport.SendAsync(inbound.Account, inbound.Sender, output.Text, output.TextStyles, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Signal message processing failed for sender {Sender}; continuing.", inbound.Sender);
            }
        }
    }
}