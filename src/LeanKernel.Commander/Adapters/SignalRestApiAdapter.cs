using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Commander.Adapters;

/// <summary>
/// Signal adapter that communicates with the signal-daemon HTTP sidecar instead of
/// spawning a signal-cli child process. Eliminates config-lock contention by moving
/// the long-lived signal-cli jsonRpc process into its own container.
/// </summary>
public sealed class SignalRestApiAdapter : ISignalAdapter
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TypingTimeout = TimeSpan.FromSeconds(5);

    private readonly string _baseUrl;
    private readonly string _account;
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly IAttachmentTextExtractionService _attachmentTextExtractor;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;

    /// <summary>
    /// Represents the on message.
    /// </summary>
    public event Action<SignalInboundMessage>? OnMessage;
    /// <summary>
    /// Represents the on error.
    /// </summary>
    public event Action<string>? OnError;

    /// <summary>
    /// Represents the signal rest api adapter.
    /// </summary>
    public SignalRestApiAdapter(
        string baseUrl,
        string account,
        HttpClient http,
        ILogger logger,
        IAttachmentTextExtractionService attachmentTextExtractor)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _account = account;
        _http = http;
        _logger = logger;
        _attachmentTextExtractor = attachmentTextExtractor;
    }

    /// <summary>
    /// Executes the start async operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        _logger.LogInformation("Signal REST adapter started (daemon: {BaseUrl}, account: {Account})", _baseUrl, _account);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Executes the send message async operation.
    /// </summary>
    /// <param name="recipient">The recipient.</param>
    /// <param name="message">The message.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task SendMessageAsync(string recipient, string message, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(SendTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var payload = new { message, number = _account, recipients = new[] { recipient } };
        using var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _http.PostAsync($"{_baseUrl}/v2/send", content, linkedCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException(
                    $"signal-daemon send returned {(int)response.StatusCode}: {body}");
            }
            _logger.LogInformation("Signal message sent to {Recipient} via daemon", recipient);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"signal-daemon /v2/send timed out after {SendTimeout.TotalSeconds:0} s");
        }
    }

    /// <summary>
    /// Executes the send typing async operation.
    /// </summary>
    /// <param name="recipient">The recipient.</param>
    /// <param name="stop">The stop.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task SendTypingAsync(string recipient, bool stop, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(TypingTimeout);
        var method = stop ? HttpMethod.Delete : HttpMethod.Put;
        var url = $"{_baseUrl}/v1/typing-indicator/{Uri.EscapeDataString(_account)}";
        var payload = new { recipient };
        using var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(method, url) { Content = content };
        try
        {
            using var response = await _http.SendAsync(request, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Typing indicator request returned {StatusCode} for {Recipient} (stop={Stop})",
                    (int)response.StatusCode,
                    recipient,
                    stop);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Typing indicator request failed (ignored): {Message}", ex.Message);
        }
    }

    // ── Receive loop ────────────────────────────────────────────────────────

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var pollUrl = $"{_baseUrl}/v1/receive/{Uri.EscapeDataString(_account)}?timeout=10";

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var response = await _http.GetAsync(pollUrl, ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("signal-daemon receive returned {Status}; retrying in 5 s",
                        (int)response.StatusCode);
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    continue;
                }

                var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(ct);
                if (items is null) continue;

                foreach (var item in items)
                    await ProcessEventAsync(item, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "signal-daemon receive loop error; retrying in 5 s");
                OnError?.Invoke(ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch { break; }
            }
        }

        _logger.LogInformation("Signal REST receive loop stopped");
    }

    private async Task ProcessEventAsync(JsonElement item, CancellationToken ct)
    {
        try
        {
            if (!item.TryGetProperty("envelope", out var envelope))
                return;

            var source = envelope.TryGetProperty("source", out var sourceProp)
                ? sourceProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(source))
                return;

            if (!envelope.TryGetProperty("dataMessage", out var dataMsg))
                return;

            var body = dataMsg.TryGetProperty("message", out var msgProp)
                ? msgProp.GetString()
                : null;

            var timestamp = GetSignalTimestamp(dataMsg, envelope);

            var attachments = await ResolveAttachmentsAsync(source, dataMsg, ct);

            if (string.IsNullOrWhiteSpace(body) && attachments.Count == 0)
                return;

            _logger.LogDebug("Signal inbound from {Sender} ({Len} chars, {AttCount} attachments)",
                source, body?.Length ?? 0, attachments.Count);

            OnMessage?.Invoke(new SignalInboundMessage
            {
                Sender = source,
                Body = body ?? string.Empty,
                TimestampMs = timestamp,
                Attachments = attachments
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing inbound signal-daemon event");
        }
    }

    private async Task<IReadOnlyList<InboundAttachment>> ResolveAttachmentsAsync(
        string sender,
        JsonElement dataMsg,
        CancellationToken ct)
    {
        if (!dataMsg.TryGetProperty("attachments", out var attArr) ||
            attArr.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<InboundAttachment>();

        foreach (var att in attArr.EnumerateArray())
        {
            if (!TryReadAttachmentMetadata(att, out var metadata))
                continue;

            var extractedText = await TryExtractAttachmentTextAsync(metadata, sender, ct);

            results.Add(new InboundAttachment
            {
                Id = metadata.Id,
                ContentType = metadata.ContentType,
                FileName = metadata.FileName,
                Caption = metadata.Caption,
                Size = metadata.Size,
                ExtractedText = extractedText
            });
        }

        return results;
    }

    private static bool TryReadAttachmentMetadata(JsonElement attachment, out AttachmentMetadata metadata)
    {
        var id = attachment.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        metadata = new AttachmentMetadata(
            Id: id ?? string.Empty,
            ContentType: attachment.TryGetProperty("contentType", out var ctProp) ? ctProp.GetString() : null,
            FileName: attachment.TryGetProperty("filename", out var fnProp) ? fnProp.GetString() : null,
            Caption: attachment.TryGetProperty("caption", out var capProp) ? capProp.GetString() : null,
            Size: attachment.TryGetProperty("size", out var szProp) && szProp.TryGetInt64(out var sz) ? sz : null);

        return !string.IsNullOrWhiteSpace(id);
    }

    private async Task<string?> TryExtractAttachmentTextAsync(
        AttachmentMetadata metadata,
        string sender,
        CancellationToken ct)
    {
        if (!_attachmentTextExtractor.CanExtractText(metadata.ContentType, metadata.FileName))
            return null;

        try
        {
            var bytes = await DownloadAttachmentAsync(metadata.Id, ct);
            return await _attachmentTextExtractor.ExtractTextAsync(
                metadata.ContentType, metadata.FileName, bytes, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download/extract attachment {Id} from {Sender}", metadata.Id, sender);
            return null;
        }
    }

    private async Task<byte[]> DownloadAttachmentAsync(string attachmentId, CancellationToken ct)
    {
        var url = $"{_baseUrl}/v1/attachments/{Uri.EscapeDataString(attachmentId)}";
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    // ── IAsyncDisposable ─────────────────────────────────────────────────────

    /// <summary>
    /// Executes the dispose async operation.
    /// </summary>
    /// <returns>The operation result.</returns>
    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_receiveLoop is not null)
        {
            try { await _receiveLoop; }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Signal receive loop cancelled during dispose");
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Receive loop exit exception during dispose"); }
        }
        _cts?.Dispose();
    }

    private static long GetSignalTimestamp(JsonElement dataMessage, JsonElement envelope)
    {
        if (dataMessage.TryGetProperty("timestamp", out var timestampProperty))
            return timestampProperty.GetInt64();

        return envelope.TryGetProperty("timestamp", out var envelopeTimestamp)
            ? envelopeTimestamp.GetInt64()
            : 0;
    }

    private sealed record AttachmentMetadata(
        string Id,
        string? ContentType,
        string? FileName,
        string? Caption,
        long? Size);
}
