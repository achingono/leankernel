using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using LeanKernel.Channels.Common.Configuration;
using LeanKernel.Data;
using LeanKernel.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels.Signal;

/// <summary>
/// Signal transport client that communicates with the signal-cli REST API and WebSocket endpoint.
/// </summary>
public sealed class SocketTransportClient(
    IHttpClientFactory httpClientFactory,
    IOptions<SignalSettings> settings,
    IChannelCredentialProvider credentials,
    ILogger<SocketTransportClient> logger) : ITransportClient
{
    private readonly Queue<InboundMessage> _pending = new();
    private readonly Queue<string> _accounts = new();
    private DateTimeOffset _lastAccountRefreshUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Receives the next inbound Signal message, fetching from the WebSocket if the pending queue is empty.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The next inbound message, or <c>null</c> if no message is available.</returns>
    public async Task<InboundMessage?> ReceiveAsync(CancellationToken ct)
    {
        if (_pending.Count > 0)
        {
            return _pending.Dequeue();
        }

        await EnsureAccountsLoadedAsync(ct);
        if (_accounts.Count == 0)
        {
            logger.LogWarning("No Signal accounts were discovered from signal-cli /v1/accounts.");
            await Task.Delay(TimeSpan.FromSeconds(settings.Value.ReconnectDelaySeconds), ct);
            return null;
        }

        var account = _accounts.Dequeue();
        _accounts.Enqueue(account);

        var payload = await ReceiveViaWebSocketAsync(account, ct);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    await EnqueueInboundIfValidAsync(item, account, ct);
                }
            }
            else
            {
                await EnqueueInboundIfValidAsync(root, account, ct);
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Signal receive returned non-JSON payload for account {Account}: {Payload}", account, payload);
        }

        return _pending.Count > 0 ? _pending.Dequeue() : null;
    }

    /// <summary>
    /// Sends a text message with optional text styles to a Signal recipient.
    /// </summary>
    /// <param name="account">The Signal account number sending the message.</param>
    /// <param name="recipient">The recipient Signal number.</param>
    /// <param name="text">The message text.</param>
    /// <param name="textStyles">The text styles to apply to the message.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SendAsync(string account, string recipient, string text, IReadOnlyList<SignalTextStyle> textStyles, CancellationToken ct)
    {
        var httpClient = httpClientFactory.CreateClient("signal-api");
        var payload = new
        {
            number = account,
            recipients = new[] { recipient },
            message = text,
            textStyles = textStyles.Count > 0 ? textStyles : null
        };

        using var response = await httpClient.PostAsJsonAsync("/v2/send", payload, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Signal send failed for account {Account} recipient {Recipient} with status {StatusCode}.",
                account,
                recipient,
                response.StatusCode);
        }
    }

    /// <summary>
    /// Sends a typing indicator start notification to the recipient.
    /// </summary>
    /// <param name="account">The Signal account number.</param>
    /// <param name="recipient">The recipient Signal number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task StartTypingAsync(string account, string recipient, CancellationToken ct) =>
        SendTypingIndicatorAsync(account, recipient, stop: false, ct);

    /// <summary>
    /// Sends a typing indicator stop notification to the recipient.
    /// </summary>
    /// <param name="account">The Signal account number.</param>
    /// <param name="recipient">The recipient Signal number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task StopTypingAsync(string account, string recipient, CancellationToken ct) =>
        SendTypingIndicatorAsync(account, recipient, stop: true, ct);

    private async Task<string?> ReceiveViaWebSocketAsync(string account, CancellationToken ct)
    {
        var wsUri = BuildReceiveUri(account);
        using var webSocket = new ClientWebSocket();

        try
        {
            await webSocket.ConnectAsync(wsUri, ct);
            return await ReadSingleMessageAsync(webSocket, ct);
        }
        catch (WebSocketException ex)
        {
            logger.LogWarning(ex, "Signal websocket receive failed for account {Account} using endpoint {Endpoint}.", account, wsUri);
            await Task.Delay(TimeSpan.FromSeconds(settings.Value.ReconnectDelaySeconds), ct);
            return null;
        }
    }

    private Uri BuildReceiveUri(string account)
    {
        var scheme = settings.Value.Port == 443 ? "wss" : "ws";
        var builder = new UriBuilder(scheme, settings.Value.Host, settings.Value.Port,
            $"/v1/receive/{Uri.EscapeDataString(account)}")
        {
            Query = $"timeout={settings.Value.ReceiveTimeoutSeconds}"
        };

        return builder.Uri;
    }

    private static async Task<string?> ReadSingleMessageAsync(ClientWebSocket webSocket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var stream = new MemoryStream();

        while (!ct.IsCancellationRequested)
        {
            var result = await webSocket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.Count > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, result.Count), ct);
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        if (stream.Length == 0)
        {
            return null;
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task EnqueueInboundIfValidAsync(JsonElement item, string account, CancellationToken ct)
    {
        if (!TryParseSignalMessage(item, out var sender, out var text, out var attachments, logger))
        {
            logger.LogTrace(
                "Rejected Signal payload for account {Account}: {Payload}",
                account,
                BuildTracePayload(item));
            return;
        }

        var token = await credentials.ResolveBearerTokenAsync(sender, ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Rejecting Signal sender {Sender}; no binding token configured.", sender);
            return;
        }

        var hydratedAttachments = await EnrichAttachmentsAsync(attachments, ct);
        _pending.Enqueue(new InboundMessage(account, sender, text, token, hydratedAttachments));
    }

    private async Task EnsureAccountsLoadedAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (_accounts.Count > 0 && now - _lastAccountRefreshUtc < TimeSpan.FromSeconds(30))
        {
            return;
        }

        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configuredAccounts = await DiscoverConfiguredAccountsAsync(ct);
        foreach (var account in configuredAccounts.Where(IsAccountName))
        {
            discovered.Add(account);
        }

        if (discovered.Count == 0)
        {
            _lastAccountRefreshUtc = now;
            return;
        }

        var existing = _accounts.ToArray();
        if (existing.Length == discovered.Count && existing.All(discovered.Contains))
        {
            _lastAccountRefreshUtc = now;
            return;
        }

        _accounts.Clear();
        foreach (var account in discovered.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            _accounts.Enqueue(account);
        }

        _lastAccountRefreshUtc = now;
    }

    private async Task<IReadOnlyList<string>> DiscoverConfiguredAccountsAsync(CancellationToken ct)
    {
        var httpClient = httpClientFactory.CreateClient("signal-api");

        try
        {
            using var response = await httpClient.GetAsync("/v1/accounts", ct);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            await using var payload = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(payload, cancellationToken: ct);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var accounts = new List<string>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        accounts.Add(value);
                    }

                    continue;
                }

                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("number", out var numberElement))
                {
                    var value = numberElement.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        accounts.Add(value);
                    }
                }
            }

            return accounts;
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "Signal account discovery from /v1/accounts failed.");
            return [];
        }
    }

    private static bool IsAccountName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Regex.IsMatch(value, "^\\+?[0-9]{7,20}$");

    private async Task SendTypingIndicatorAsync(string account, string recipient, bool stop, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(recipient))
        {
            return;
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, settings.Value.TypingRequestTimeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            var httpClient = httpClientFactory.CreateClient("signal-api");
            using var request = new HttpRequestMessage(
                stop ? HttpMethod.Delete : HttpMethod.Put,
                $"/v1/typing-indicator/{Uri.EscapeDataString(account)}")
            {
                Content = JsonContent.Create(new { recipient })
            };

            using var response = await httpClient.SendAsync(request, linkedCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "Signal typing indicator returned {StatusCode} for account {Account} recipient {Recipient} (stop={Stop}).",
                    (int)response.StatusCode,
                    account,
                    recipient,
                    stop);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogDebug(ex,
                "Signal typing indicator request failed for account {Account} recipient {Recipient} (stop={Stop}).",
                account,
                recipient,
                stop);
        }
    }

    private static bool TryParseSignalMessage(
        JsonElement item,
        out string sender,
        out string text,
        out IReadOnlyList<InboundAttachment> attachments,
        ILogger logger)
    {
        sender = string.Empty;
        text = string.Empty;
        attachments = [];

        if (!item.TryGetProperty("envelope", out var envelope))
        {
            logger.LogDebug("Signal message rejected: payload has no 'envelope' property.");
            return false;
        }

        if (!envelope.TryGetProperty("sourceNumber", out var sourceNumberElement)
            || string.IsNullOrWhiteSpace(sourceNumberElement.GetString()))
        {
            logger.LogWarning("Signal message rejected: 'envelope.sourceNumber' is missing or empty.");
            return false;
        }

        sender = sourceNumberElement.GetString()!;

        JsonElement dataMessage;
        if (envelope.TryGetProperty("dataMessage", out var envelopeDataMessage))
        {
            dataMessage = envelopeDataMessage;
        }
        else if (envelope.TryGetProperty("syncMessage", out var syncMessage)
                 && syncMessage.TryGetProperty("sentMessage", out var sentMessage))
        {
            dataMessage = sentMessage;
        }
        else
        {
            logger.LogWarning("Signal message rejected from {Sender}: no 'dataMessage' or 'syncMessage.sentMessage' in envelope.", sender);
            return false;
        }

        text = dataMessage.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? string.Empty
            : string.Empty;

        attachments = ParseInboundAttachments(dataMessage);

        if (string.IsNullOrWhiteSpace(text) && attachments.Count > 0)
        {
            text = "[non-text Signal message with attachment metadata]";
        }

        if (string.IsNullOrWhiteSpace(text) && attachments.Count == 0)
        {
            logger.LogDebug("Signal message from {Sender} rejected: message text is empty.", sender);
        }

        return !string.IsNullOrWhiteSpace(text) || attachments.Count > 0;
    }

    private static IReadOnlyList<InboundAttachment> ParseInboundAttachments(JsonElement dataMessage)
    {
        if (!dataMessage.TryGetProperty("attachments", out var attachmentsElement)
            || attachmentsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var attachments = new List<InboundAttachment>();

        foreach (var attachment in attachmentsElement.EnumerateArray())
        {
            if (attachment.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var attachmentId = TryReadAttachmentId(attachment);

            var contentType = attachment.TryGetProperty("contentType", out var contentTypeElement)
                ? contentTypeElement.GetString() ?? string.Empty
                : string.Empty;
            var fileName = attachment.TryGetProperty("filename", out var filenameElement)
                ? filenameElement.GetString() ?? string.Empty
                : string.Empty;

            attachments.Add(new InboundAttachment(attachmentId, contentType, fileName, string.Empty));
        }

        return attachments;
    }

    private static string TryReadAttachmentId(JsonElement attachment)
    {
        if (attachment.TryGetProperty("id", out var idElement))
        {
            return ReadAttachmentIdValue(idElement);
        }

        if (attachment.TryGetProperty("attachmentId", out var attachmentIdElement))
        {
            return ReadAttachmentIdValue(attachmentIdElement);
        }

        return string.Empty;
    }

    private static string ReadAttachmentIdValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };

    private static string BuildTracePayload(JsonElement item)
    {
        const int maxChars = 4000;

        var raw = item.GetRawText();
        if (raw.Length <= maxChars)
        {
            return raw;
        }

        return $"{raw[..maxChars]}...(truncated)";
    }

    private async Task<IReadOnlyList<InboundAttachment>> EnrichAttachmentsAsync(
        IReadOnlyList<InboundAttachment> attachments,
        CancellationToken ct)
    {
        if (attachments.Count == 0)
        {
            return attachments;
        }

        var maxImagesPerMessage = Math.Max(0, settings.Value.MaxImageAttachmentsPerMessage);
        var maxImageAttachmentBytes = settings.Value.MaxImageAttachmentBytes;

        if (maxImagesPerMessage == 0 || maxImageAttachmentBytes <= 0)
        {
            return attachments;
        }

        var enriched = new List<InboundAttachment>(attachments.Count);
        var forwardedCount = 0;

        foreach (var attachment in attachments)
        {
            if (!attachment.IsImage
                || string.IsNullOrWhiteSpace(attachment.AttachmentId)
                || forwardedCount >= maxImagesPerMessage)
            {
                enriched.Add(attachment);
                continue;
            }

            var imageDataUrl = await TryDownloadImageDataUrlAsync(attachment, maxImageAttachmentBytes, ct);
            if (string.IsNullOrWhiteSpace(imageDataUrl))
            {
                enriched.Add(attachment);
                continue;
            }

            enriched.Add(attachment with { ImageDataUrl = imageDataUrl });
            forwardedCount++;
        }

        return enriched;
    }

    private async Task<string> TryDownloadImageDataUrlAsync(InboundAttachment attachment, int maxAttachmentBytes, CancellationToken ct)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient("signal-api");

            using var response = await httpClient.GetAsync($"/v1/attachments/{Uri.EscapeDataString(attachment.AttachmentId)}", ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "Signal attachment download failed for id {AttachmentId} with status {StatusCode}.",
                    attachment.AttachmentId,
                    response.StatusCode);
                return string.Empty;
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > maxAttachmentBytes)
            {
                logger.LogInformation(
                    "Skipping Signal attachment {AttachmentId}: size {SizeBytes} exceeds limit {LimitBytes}.",
                    attachment.AttachmentId,
                    contentLength.Value,
                    maxAttachmentBytes);
                return string.Empty;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            if (bytes.Length > maxAttachmentBytes)
            {
                logger.LogInformation(
                    "Skipping Signal attachment {AttachmentId}: downloaded size {SizeBytes} exceeds limit {LimitBytes}.",
                    attachment.AttachmentId,
                    bytes.Length,
                    maxAttachmentBytes);
                return string.Empty;
            }

            var mediaType = !string.IsNullOrWhiteSpace(attachment.ContentType)
                ? attachment.ContentType
                : response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var base64 = Convert.ToBase64String(bytes);
            return $"data:{mediaType};base64,{base64}";
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "Signal attachment download failed for id {AttachmentId} due to HTTP error.", attachment.AttachmentId);
            return string.Empty;
        }
    }
}
