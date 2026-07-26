using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Channels;

using LeanKernel.Channels.Teams.Services;
using LeanKernel.Entities;

using Microsoft.Extensions.Options;

namespace LeanKernel.Channels.Teams.Clients;

/// <summary>Transport client that communicates with the Bot Framework connector API.</summary>
public sealed class BotFrameworkTransportClient(
    IHttpClientFactory httpClientFactory,
    IOptions<BotSettings> settings,
    IChannelCredentialProvider credentialProvider,
    ILogger<BotFrameworkTransportClient> logger) : ITransportClient
{
    private readonly Channel<InboundActivity> _channel = Channel.CreateUnbounded<InboundActivity>();
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string _connectorToken = string.Empty;
    private DateTimeOffset _connectorTokenExpiresAt = DateTimeOffset.MinValue;

    /// <summary>Receives the next inbound activity, resolving its bearer token.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The inbound activity with a resolved bearer token, or <c>null</c> if no activity is available.</returns>
    public async Task<InboundActivity?> ReceiveAsync(CancellationToken ct)
    {
        if (await _channel.Reader.WaitToReadAsync(ct) && _channel.Reader.TryRead(out var activity))
        {
            var token = await credentialProvider.ResolveBearerTokenAsync(activity.SenderId, ct);
            var hydratedAttachments = await EnrichAttachmentsAsync(activity.Attachments, ct);
            return activity with { BearerToken = token, Attachments = hydratedAttachments };
        }

        return null;
    }

    private async Task<IReadOnlyList<ChannelAttachmentEnvelope>> EnrichAttachmentsAsync(
        IReadOnlyList<ChannelAttachmentEnvelope> attachments,
        CancellationToken ct)
    {
        if (attachments.Count == 0)
        {
            return attachments;
        }

        var maxAttachmentBytes = Math.Max(0, settings.Value.MaxAttachmentBytes);
        if (maxAttachmentBytes <= 0)
        {
            return attachments;
        }

        var maxImagesPerMessage = Math.Max(0, settings.Value.MaxImageAttachmentsPerMessage);
        var maxFilesPerMessage = Math.Max(0, settings.Value.MaxFileAttachmentsPerMessage);
        var connectorToken = await GetConnectorTokenAsync(ct);
        var enriched = new List<ChannelAttachmentEnvelope>(attachments.Count);
        var imageCount = 0;
        var fileCount = 0;

        foreach (var attachment in attachments)
        {
            if (attachment.HasImageData || attachment.HasFileData)
            {
                enriched.Add(attachment);
                continue;
            }

            if (!Uri.TryCreate(attachment.AttachmentId, UriKind.Absolute, out var attachmentUri))
            {
                enriched.Add(attachment);
                continue;
            }

            if (!TryShouldDownload(attachment, maxImagesPerMessage, maxFilesPerMessage, imageCount, fileCount))
            {
                enriched.Add(attachment);
                continue;
            }

            var dataUrl = await TryDownloadAttachmentAsDataUrlAsync(attachment, attachmentUri, connectorToken, maxAttachmentBytes, ct);
            if (string.IsNullOrWhiteSpace(dataUrl))
            {
                enriched.Add(attachment);
                continue;
            }

            if (attachment.IsImage)
            {
                enriched.Add(attachment with { ImageDataUrl = dataUrl });
                imageCount++;
                continue;
            }

            enriched.Add(attachment with { FileDataUrl = dataUrl });
            fileCount++;
        }

        return enriched;
    }

    private static bool TryShouldDownload(
        ChannelAttachmentEnvelope attachment,
        int maxImagesPerMessage,
        int maxFilesPerMessage,
        int imageCount,
        int fileCount)
    {
        if (attachment.IsImage)
        {
            return imageCount < maxImagesPerMessage;
        }

        return fileCount < maxFilesPerMessage;
    }

    private async Task<string> TryDownloadAttachmentAsDataUrlAsync(
        ChannelAttachmentEnvelope attachment,
        Uri attachmentUri,
        string connectorToken,
        int maxAttachmentBytes,
        CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("teams-connector");
            using var request = new HttpRequestMessage(HttpMethod.Get, attachmentUri);
            if (!string.IsNullOrWhiteSpace(connectorToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(Constants.Http.Headers.Bearer, connectorToken);
            }

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "Teams attachment download failed for {AttachmentId} with status {StatusCode}.",
                    attachment.AttachmentId,
                    response.StatusCode);
                return string.Empty;
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > maxAttachmentBytes)
            {
                logger.LogInformation(
                    "Skipping Teams attachment {AttachmentId}: size {SizeBytes} exceeds limit {LimitBytes}.",
                    attachment.AttachmentId,
                    contentLength.Value,
                    maxAttachmentBytes);
                return string.Empty;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0 || bytes.Length > maxAttachmentBytes)
            {
                return string.Empty;
            }

            var mediaType = !string.IsNullOrWhiteSpace(attachment.ContentType)
                ? attachment.ContentType
                : response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var base64 = Convert.ToBase64String(bytes);
            return $"data:{mediaType};base64,{base64}";
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "Teams attachment download failed for {AttachmentId}.", attachment.AttachmentId);
            return string.Empty;
        }
    }

    /// <summary>Enqueues an inbound activity for processing.</summary>
    /// <param name="activity">The activity to enqueue.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task EnqueueAsync(InboundActivity activity, CancellationToken ct) =>
        _channel.Writer.WriteAsync(activity, ct).AsTask();

    /// <summary>Sends a reply message to the Teams conversation.</summary>
    /// <param name="inboundActivity">The original inbound activity to reply to.</param>
    /// <param name="text">The reply text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SendAsync(InboundActivity inboundActivity, string text, CancellationToken ct)
    {
        if (!IsTrustedServiceUrl(inboundActivity.ServiceUrl, settings.Value.AllowedServiceUrlHostSuffixes))
        {
            logger.LogWarning("Skipping Teams reply because service URL is not trusted: {ServiceUrl}", inboundActivity.ServiceUrl);
            return;
        }

        var connectorToken = await GetConnectorTokenAsync(ct);
        if (string.IsNullOrWhiteSpace(connectorToken))
        {
            logger.LogWarning("Skipping Teams reply because connector token could not be acquired.");
            return;
        }

        var connectorClient = httpClientFactory.CreateClient("teams-connector");
        connectorClient.BaseAddress = new Uri(inboundActivity.ServiceUrl);
        connectorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(Constants.Http.Headers.Bearer, connectorToken);

        var activity = new
        {
            type = "message",
            text,
            replyToId = inboundActivity.ActivityId,
            conversation = new { id = inboundActivity.ConversationId },
            from = new { id = settings.Value.AppId },
            recipient = new { id = inboundActivity.SenderId }
        };

        using var response = await connectorClient.PostAsJsonAsync(
            $"/v3/conversations/{Uri.EscapeDataString(inboundActivity.ConversationId)}/activities",
            activity,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Teams send failed with status {StatusCode} for conversation {ConversationId}.", response.StatusCode, inboundActivity.ConversationId);
        }
    }

    private async Task<string> GetConnectorTokenAsync(CancellationToken ct)
    {
        if (_connectorTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2) && !string.IsNullOrWhiteSpace(_connectorToken))
        {
            return _connectorToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_connectorTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2) && !string.IsNullOrWhiteSpace(_connectorToken))
            {
                return _connectorToken;
            }

            var authClient = httpClientFactory.CreateClient("teams-auth");
            using var payload = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = settings.Value.AppId,
                ["client_secret"] = settings.Value.AppPassword,
                ["scope"] = "https://api.botframework.com/.default"
            });

            using var response = await authClient.PostAsync("/botframework.com/oauth2/v2.0/token", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Teams connector token request failed with status {StatusCode}.", response.StatusCode);
                return string.Empty;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            _connectorToken = document.RootElement.GetProperty("access_token").GetString() ?? string.Empty;
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expires)
                ? expires.GetInt32()
                : 300;
            _connectorTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _connectorToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static bool IsTrustedServiceUrl(string value, IReadOnlyCollection<string> allowedHostSuffixes)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var suffix in allowedHostSuffixes)
        {
            if (string.IsNullOrWhiteSpace(suffix))
            {
                continue;
            }

            if (uri.Host.EndsWith(suffix.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}