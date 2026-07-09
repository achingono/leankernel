using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels;

/// <summary>
/// Provides functionality for signal channel.
/// </summary>
public sealed class SignalChannel : IChannel
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const int MaxSignalMessageChars = 3500;
    private static readonly TimeSpan SignalSendTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SignalTypingTimeout = TimeSpan.FromSeconds(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SignalChannelConfig _config;
    private readonly ILogger<SignalChannel> _logger;
    private readonly ConcurrentDictionary<string, string> _senderNumber = new(StringComparer.Ordinal);
    private readonly object _syncRoot = new();

    private CancellationTokenSource? _pollingCts;
    private readonly List<Task> _pollingTasks = [];
    private int _connectedCount;

    public SignalChannel(
        IHttpClientFactory httpClientFactory,
        IOptions<ChannelsConfig> config,
        ILogger<SignalChannel> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _config = (config ?? throw new ArgumentNullException(nameof(config))).Value.Signal;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets channel id.
    /// </summary>
    public string ChannelId => "signal";

    public bool IsConnected => Volatile.Read(ref _connectedCount) > 0;

    public event Func<ChannelMessage, Task>? MessageReceived;

    /// <summary>
    /// Starts async.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>The operation result.</returns>
    public Task StartAsync(CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("Signal channel is disabled; startup skipped");
            return Task.CompletedTask;
        }

        var numbers = _config.GetPhoneNumbers();
        if (numbers.Count == 0)
        {
            _logger.LogWarning("Signal channel is enabled but no phone numbers are configured; startup skipped");
            return Task.CompletedTask;
        }

        lock (_syncRoot)
        {
            if (_pollingCts is not null)
            {
                return Task.CompletedTask;
            }

            _pollingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            foreach (var number in numbers)
            {
                var captured = number;
                _pollingTasks.Add(
                    Task.Run(() => WebSocketLoopAsync(captured, _pollingCts.Token), CancellationToken.None));
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops async.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>The operation result.</returns>
    public async Task StopAsync(CancellationToken ct = default)
    {
        List<Task> tasks;
        CancellationTokenSource? cts;

        lock (_syncRoot)
        {
            cts = _pollingCts;
            tasks = [.. _pollingTasks];
            _pollingCts = null;
            _pollingTasks.Clear();
            _connectedCount = 0;
        }

        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
            await Task.WhenAll(tasks).WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task SendAsync(string recipientId, string message, IReadOnlyList<Attachment>? attachments = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientId);
        if (string.IsNullOrWhiteSpace(message) && (attachments is null || attachments.Count == 0))
        {
            throw new ArgumentException("Message or attachments must be provided.", nameof(message));
        }

        message ??= string.Empty;

        var sourceNumber = GetSourceNumber(recipientId);
        using var timeoutCts = new CancellationTokenSource(SignalSendTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var client = _httpClientFactory.CreateClient("signal-daemon");
        var encodedAttachments = EncodeSignalAttachments(attachments);

        if (encodedAttachments.Count > 0)
        {
            using var response = await client.PostAsJsonAsync(
                "/v2/send",
                new SignalSendRequest
                {
                    Number = sourceNumber,
                    Recipients = [recipientId],
                    Message = message,
                    Base64Attachments = encodedAttachments
                },
                SerializerOptions,
                linkedCts.Token).ConfigureAwait(false);

            await EnsureSignalSendSucceededAsync(response, recipientId, sourceNumber, ct).ConfigureAwait(false);
            return;
        }

        var chunks = SplitMessage(message);
        if (chunks.Count > 1)
        {
            _logger.LogWarning(
                "Signal reply to {RecipientId} exceeded {MaxChars} characters; splitting into {ChunkCount} messages",
                recipientId,
                MaxSignalMessageChars,
                chunks.Count);
        }

        foreach (var chunk in chunks)
        {
            using var response = await client.PostAsJsonAsync(
                "/v2/send",
                new SignalSendRequest
                {
                    Number = sourceNumber,
                    Recipients = [recipientId],
                    Message = chunk
                },
                SerializerOptions,
                linkedCts.Token).ConfigureAwait(false);

            await EnsureSignalSendSucceededAsync(response, recipientId, sourceNumber, ct).ConfigureAwait(false);
        }
    }

    private async Task EnsureSignalSendSucceededAsync(HttpResponseMessage response, string recipientId, string sourceNumber, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var statusCode = (int)response.StatusCode;

        if (statusCode == 429)
        {
            _logger.LogError(
                "Signal send rate-limited (429) for {RecipientId} from {SourceNumber}: {Body}",
                recipientId, sourceNumber, body);
            _logger.LogError(
                "Resolve via POST /v1/accounts/{{number}}/rate-limit-challenge with a captcha");
        }
        else
        {
            _logger.LogError(
                "Signal send failed ({StatusCode}) for {RecipientId} from {SourceNumber}: {Body}",
                statusCode, recipientId, sourceNumber, body);
        }

        response.EnsureSuccessStatusCode();
    }

    private static List<string> EncodeSignalAttachments(IReadOnlyList<Attachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return [];
        }

        var encoded = new List<string>(attachments.Count);
        foreach (var attachment in attachments)
        {
            var mimeType = string.IsNullOrWhiteSpace(attachment.ContentType)
                ? "application/octet-stream"
                : attachment.ContentType;
            var fileName = Uri.EscapeDataString(string.IsNullOrWhiteSpace(attachment.FileName) ? "attachment.bin" : attachment.FileName);
            var base64Data = Convert.ToBase64String(attachment.Data);
            encoded.Add($"data:{mimeType};filename={fileName};base64,{base64Data}");
        }

        return encoded;
    }

    /// <summary>
    /// Starts typing async.
    /// </summary>
    /// <param name="recipientId">The recipient id.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>The operation result.</returns>
    public Task StartTypingAsync(string recipientId, CancellationToken ct = default)
        => SendTypingIndicatorAsync(recipientId, stop: false, ct);

    /// <summary>
    /// Stops typing async.
    /// </summary>
    /// <param name="recipientId">The recipient id.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>The operation result.</returns>
    public Task StopTypingAsync(string recipientId, CancellationToken ct = default)
        => SendTypingIndicatorAsync(recipientId, stop: true, ct);

    private async Task WebSocketLoopAsync(string phoneNumber, CancellationToken ct)
    {
        var wsBase = _config.DaemonUrl
            .Replace("http://", "ws://")
            .Replace("https://", "wss://");
        var wsUri = new Uri($"{wsBase}/v1/receive/{Uri.EscapeDataString(phoneNumber)}");

        var reconnectAttempts = 0;
        var wasEverConnected = false;

        while (!ct.IsCancellationRequested)
        {
            var connectTime = DateTimeOffset.UtcNow;

            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(wsUri, ct).ConfigureAwait(false);

                Interlocked.Increment(ref _connectedCount);
                wasEverConnected = true;
                reconnectAttempts = 0;
                _logger.LogInformation(
                    "Signal WebSocket connected for {PhoneNumber}", phoneNumber);

                await ProcessWebSocketMessagesAsync(ws, phoneNumber, connectTime, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LogCancellation(phoneNumber, connectTime, wasEverConnected);
                break;
            }
            catch (Exception ex)
            {
                if (!TryHandleReconnect(ex, phoneNumber, connectTime, ref wasEverConnected, ref reconnectAttempts))
                {
                    break;
                }
            }
        }

        if (wasEverConnected)
        {
            Interlocked.Decrement(ref _connectedCount);
        }
    }

    private async Task ProcessWebSocketMessagesAsync(ClientWebSocket ws, string phoneNumber, DateTimeOffset connectTime, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var messageBuffer = new StringBuilder();

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                var elapsed = DateTimeOffset.UtcNow - connectTime;
                _logger.LogInformation(
                    "Signal WebSocket closed normally for {PhoneNumber} after {Elapsed}",
                    phoneNumber,
                    elapsed);
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None)
                    .ConfigureAwait(false);
                break;
            }

            messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage)
            {
                await DispatchIncomingMessageAsync(messageBuffer, phoneNumber, ct);
            }
        }
    }

    private async Task DispatchIncomingMessageAsync(StringBuilder messageBuffer, string phoneNumber, CancellationToken ct)
    {
        var json = messageBuffer.ToString();
        messageBuffer.Clear();

        var envelope = JsonSerializer.Deserialize<SignalReceiveEnvelope>(json, SerializerOptions);
        var channelMessage = await CreateChannelMessageAsync(envelope, phoneNumber, ct).ConfigureAwait(false);
        if (channelMessage is not null)
        {
            _senderNumber[channelMessage.SenderId] = phoneNumber;
            await DispatchMessageAsync(channelMessage, ct).ConfigureAwait(false);
        }
    }

    private void LogCancellation(string phoneNumber, DateTimeOffset connectTime, bool wasEverConnected)
    {
        if (!wasEverConnected)
        {
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - connectTime;
        _logger.LogInformation(
            "Signal WebSocket cancelled for {PhoneNumber} after {Elapsed}",
            phoneNumber,
            elapsed);
    }

    private bool TryHandleReconnect(Exception ex, string phoneNumber, DateTimeOffset connectTime, ref bool wasEverConnected, ref int reconnectAttempts)
    {
        reconnectAttempts++;

        if (wasEverConnected)
        {
            var elapsed = DateTimeOffset.UtcNow - connectTime;
            _logger.LogWarning(
                ex,
                "Signal WebSocket disconnected for {PhoneNumber} after {Elapsed} (attempt {Attempt}/{MaxAttempts})",
                phoneNumber,
                elapsed,
                reconnectAttempts,
                _config.MaxReconnectAttempts);
            Interlocked.Decrement(ref _connectedCount);
            wasEverConnected = false;
        }
        else
        {
            _logger.LogWarning(
                ex,
                "Signal WebSocket connection failed for {PhoneNumber} (attempt {Attempt}/{MaxAttempts})",
                phoneNumber,
                reconnectAttempts,
                _config.MaxReconnectAttempts);
        }

        if (_config.MaxReconnectAttempts > 0 && reconnectAttempts >= _config.MaxReconnectAttempts)
        {
            _logger.LogError(
                "Signal channel reached the maximum reconnect attempts for {PhoneNumber} and will stop",
                phoneNumber);
            return false;
        }

        var reconnectDelay = GetReconnectDelay(reconnectAttempts);
        _logger.LogInformation(
            "Signal WebSocket reconnecting for {PhoneNumber} in {Delay} (attempt {Attempt}/{MaxAttempts})",
            phoneNumber,
            reconnectDelay,
            reconnectAttempts,
            _config.MaxReconnectAttempts);

        if (reconnectDelay > TimeSpan.Zero)
        {
            Task.Delay(reconnectDelay, CancellationToken.None).Wait();
        }

        return true;
    }

    private async Task DispatchMessageAsync(ChannelMessage message, CancellationToken ct)
    {
        var handler = MessageReceived;
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList().Cast<Func<ChannelMessage, Task>>())
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await subscriber(message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Signal channel subscriber failed while handling a message from {SenderId}",
                    message.SenderId);
            }
        }
    }

    private async Task<ChannelMessage?> CreateChannelMessageAsync(SignalReceiveEnvelope? item, string recipientId, CancellationToken ct)
    {
        if (item is null) return null;

        var envelope = item.Envelope;
        var senderId = envelope?.SourceNumber ?? envelope?.Source;
        var attachments = await LoadAttachmentsAsync(envelope?.DataMessage?.Attachments, ct).ConfigureAwait(false);
        var content = envelope?.DataMessage?.Message;

        if (string.IsNullOrWhiteSpace(senderId))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            if (attachments.Count == 0)
            {
                return null;
            }

            content = BuildAttachmentFallbackContent(attachments);
        }

        return new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = senderId,
            RecipientId = recipientId,
            Content = content,
            Attachments = attachments.Count == 0 ? null : attachments,
            Timestamp = envelope?.Timestamp is > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(envelope.Timestamp.Value)
                : DateTimeOffset.UtcNow
        };
    }

    private async Task<List<Attachment>> LoadAttachmentsAsync(IReadOnlyList<SignalAttachment>? signalAttachments, CancellationToken ct)
    {
        if (signalAttachments is null || signalAttachments.Count == 0)
        {
            return [];
        }

        var client = _httpClientFactory.CreateClient("signal-daemon");
        var attachments = new List<Attachment>(signalAttachments.Count);

        foreach (var signalAttachment in signalAttachments)
        {
            var attachment = await DownloadSingleAttachmentAsync(client, signalAttachment, ct).ConfigureAwait(false);
            if (attachment is not null)
            {
                attachments.Add(attachment);
            }
        }

        return attachments;
    }

    private async Task<Attachment?> DownloadSingleAttachmentAsync(HttpClient client, SignalAttachment signalAttachment, CancellationToken ct)
    {
        var attachmentId = signalAttachment.Id?.Trim();
        if (string.IsNullOrWhiteSpace(attachmentId))
        {
            return null;
        }

        try
        {
            using var response = await client.GetAsync($"/v1/attachments/{Uri.EscapeDataString(attachmentId)}", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Signal attachment download failed for {AttachmentId}: {StatusCode}",
                    attachmentId,
                    (int)response.StatusCode);
                return null;
            }

            var contentType = signalAttachment.ContentType;
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = response.Content.Headers.ContentType?.MediaType;
            }

            return new Attachment
            {
                FileName = string.IsNullOrWhiteSpace(signalAttachment.Filename) ? attachmentId : signalAttachment.Filename,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                Data = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Signal attachment download failed for {AttachmentId}", attachmentId);
            return null;
        }
    }

    private static string BuildAttachmentFallbackContent(IReadOnlyList<Attachment> attachments)
    {
        if (attachments.Count == 1)
        {
            return $"[Received attachment: {attachments[0].FileName}]";
        }

        var names = attachments
            .Take(3)
            .Select(attachment => attachment.FileName)
            .ToArray();

        var namesText = string.Join(", ", names);
        if (attachments.Count > names.Length)
        {
            namesText = $"{namesText}, +{attachments.Count - names.Length} more";
        }

        return $"[Received attachments: {namesText}]";
    }

    private TimeSpan GetReconnectDelay(int reconnectAttempt)
    {
        if (_config.ReconnectDelaySeconds <= 0)
        {
            return TimeSpan.Zero;
        }

        var multiplier = Math.Pow(2, Math.Max(0, reconnectAttempt - 1));
        var totalSeconds = _config.ReconnectDelaySeconds * multiplier;
        return TimeSpan.FromSeconds(Math.Min(totalSeconds, 300));
    }

    private string GetSourceNumber(string recipientId)
    {
        var numbers = _config.GetPhoneNumbers();
        if (numbers.Count == 0)
        {
            throw new InvalidOperationException("No phone numbers configured for Signal channel");
        }

        return _senderNumber.TryGetValue(recipientId, out var mapped)
            ? mapped
            : numbers[0];
    }

    private async Task SendTypingIndicatorAsync(string recipientId, bool stop, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientId);

        try
        {
            var sourceNumber = GetSourceNumber(recipientId);
            using var timeoutCts = new CancellationTokenSource(SignalTypingTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            var client = _httpClientFactory.CreateClient("signal-daemon");
            using var request = new HttpRequestMessage(
                stop ? HttpMethod.Delete : HttpMethod.Put,
                $"/v1/typing-indicator/{Uri.EscapeDataString(sourceNumber)}")
            {
                Content = JsonContent.Create(new { recipient = recipientId }, options: SerializerOptions)
            };

            using var response = await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Signal typing indicator returned {StatusCode} for {RecipientId} (stop={Stop})",
                    (int)response.StatusCode,
                    recipientId,
                    stop);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(
                ex,
                "Signal typing indicator request failed (ignored) for {RecipientId} (stop={Stop})",
                recipientId,
                stop);
        }
    }

    private static List<string> SplitMessage(string message)
    {
        if (message.Length <= MaxSignalMessageChars)
        {
            return [message];
        }

        var chunks = new List<string>((message.Length + MaxSignalMessageChars - 1) / MaxSignalMessageChars);
        for (var index = 0; index < message.Length; index += MaxSignalMessageChars)
        {
            var length = Math.Min(MaxSignalMessageChars, message.Length - index);
            chunks.Add(message.Substring(index, length));
        }

        return chunks;
    }
}

/// <summary>
/// Provides functionality for signal receive envelope.
/// </summary>
internal sealed class SignalReceiveEnvelope
{
    [JsonPropertyName("envelope")]
    /// <summary>
    /// Gets or sets envelope.
    /// </summary>
    public SignalEnvelope? Envelope { get; set; }

    [JsonPropertyName("account")]
    /// <summary>
    /// Gets or sets account.
    /// </summary>
    public string? Account { get; set; }
}

/// <summary>
/// Provides functionality for signal envelope.
/// </summary>
internal sealed class SignalEnvelope
{
    [JsonPropertyName("sourceNumber")]
    /// <summary>
    /// Gets or sets source number.
    /// </summary>
    public string? SourceNumber { get; set; }

    [JsonPropertyName("source")]
    /// <summary>
    /// Gets or sets source.
    /// </summary>
    public string? Source { get; set; }

    [JsonPropertyName("timestamp")]
    /// <summary>
    /// Gets or sets timestamp.
    /// </summary>
    public long? Timestamp { get; set; }

    [JsonPropertyName("dataMessage")]
    /// <summary>
    /// Gets or sets data message.
    /// </summary>
    public SignalDataMessage? DataMessage { get; set; }
}

/// <summary>
/// Provides functionality for signal data message.
/// </summary>
internal sealed class SignalDataMessage
{
    [JsonPropertyName("message")]
    /// <summary>
    /// Gets or sets message.
    /// </summary>
    public string? Message { get; set; }

    [JsonPropertyName("attachments")]
    /// <summary>
    /// Gets or sets attachments.
    /// </summary>
    public List<SignalAttachment>? Attachments { get; set; }
}

/// <summary>
/// Provides functionality for signal attachment metadata.
/// </summary>
internal sealed class SignalAttachment
{
    [JsonPropertyName("id")]
    /// <summary>
    /// Gets or sets id.
    /// </summary>
    public string? Id { get; set; }

    [JsonPropertyName("filename")]
    /// <summary>
    /// Gets or sets filename.
    /// </summary>
    public string? Filename { get; set; }

    [JsonPropertyName("contentType")]
    /// <summary>
    /// Gets or sets content type.
    /// </summary>
    public string? ContentType { get; set; }
}

/// <summary>
/// Provides functionality for signal send request.
/// </summary>
internal sealed class SignalSendRequest
{
    [JsonPropertyName("number")]
    /// <summary>
    /// Gets or sets number.
    /// </summary>
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("recipients")]
    /// <summary>
    /// Gets or sets recipients.
    /// </summary>
    public List<string> Recipients { get; set; } = [];

    [JsonPropertyName("message")]
    /// <summary>
    /// Gets or sets message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("base64_attachments")]
    /// <summary>
    /// Gets or sets base64 encoded attachments.
    /// </summary>
    public List<string>? Base64Attachments { get; set; }
}
