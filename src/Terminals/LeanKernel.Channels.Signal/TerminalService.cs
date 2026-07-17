using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LeanKernel.Channels.Common.Credentials;
using LeanKernel.Channels.Common.Settings;
using LeanKernel.Data;
using LeanKernel.Entities;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Channels.Signal;

public sealed class TerminalService(
    ILogger<TerminalService> logger,
    ITransportClient transport,
    GatewayChannelClient gatewayClient,
    IOptions<SignalSettings> signalSettings) : BackgroundService
{
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

public sealed class GatewayChannelClient(HttpClient httpClient, IOptions<GatewaySettings> settings)
{
    public async Task<GatewayTurnResult> RunTurnAsync(object input, string bearerToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = settings.Value.Model,
            input,
            agent = new
            {
                name = settings.Value.AgentName
            }
        }), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return new GatewayTurnResult($"Gateway request failed: {(int)response.StatusCode}", []);

        var payload = await response.Content.ReadAsStringAsync(ct);
        return ExtractResponseText(payload);
    }

    private static GatewayTurnResult ExtractResponseText(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return new GatewayTurnResult(string.Empty, []);

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.TryGetProperty("output", out var output)
                && output.ValueKind == JsonValueKind.Array)
            {
                var builder = new StringBuilder();
                var styles = new List<SignalTextStyle>();

                foreach (var outputItem in output.EnumerateArray())
                {
                    if (!outputItem.TryGetProperty("content", out var content)
                        || content.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var contentItem in content.EnumerateArray())
                    {
                        if (!contentItem.TryGetProperty("type", out var typeElement)
                            || !string.Equals(typeElement.GetString(), "output_text", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var text = contentItem.TryGetProperty("text", out var textElement)
                            ? textElement.GetString()
                            : null;

                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        var explicitStyles = ParseTextStyles(contentItem, text.Length);
                        var renderedText = text;
                        IReadOnlyList<SignalTextStyle> segmentStyles = explicitStyles;

                        if (segmentStyles.Count == 0)
                        {
                            var markdownFallback = ParseMarkdownTextStyles(text);
                            renderedText = markdownFallback.Text;
                            segmentStyles = markdownFallback.TextStyles;
                        }

                        var offset = builder.Length;
                        if (builder.Length > 0)
                            builder.AppendLine();

                        offset = builder.Length;
                        builder.Append(renderedText);

                        foreach (var segmentStyle in segmentStyles)
                        {
                            styles.Add(new SignalTextStyle
                            {
                                Start = offset + segmentStyle.Start,
                                Length = segmentStyle.Length,
                                Style = segmentStyle.Style
                            });
                        }
                    }
                }

                if (builder.Length > 0)
                    return new GatewayTurnResult(builder.ToString(), styles);
            }
        }
        catch (JsonException)
        {
            // Fallback to raw payload for non-JSON responses.
        }

        return new GatewayTurnResult(payload, []);
    }

    private static IReadOnlyList<SignalTextStyle> ParseTextStyles(JsonElement contentItem, int segmentLength)
    {
        var mappedStyles = new List<SignalTextStyle>();

        if (contentItem.TryGetProperty("textStyles", out var textStylesElement)
            && textStylesElement.ValueKind == JsonValueKind.Array)
        {
            AddMappedStyles(mappedStyles, textStylesElement, segmentLength);
        }

        if (contentItem.TryGetProperty("annotations", out var annotationsElement)
            && annotationsElement.ValueKind == JsonValueKind.Array)
        {
            AddMappedStyles(mappedStyles, annotationsElement, segmentLength);
        }

        return mappedStyles;
    }

    private static void AddMappedStyles(List<SignalTextStyle> mappedStyles, JsonElement styleArray, int segmentLength)
    {
        foreach (var styleItem in styleArray.EnumerateArray())
        {
            if (!TryMapSignalStyle(styleItem, out var style)
                || !TryReadRange(styleItem, out var start, out var length))
            {
                continue;
            }

            if (start < 0 || length <= 0 || start >= segmentLength)
                continue;

            var boundedLength = Math.Min(length, segmentLength - start);
            if (boundedLength <= 0)
                continue;

            mappedStyles.Add(new SignalTextStyle
            {
                Start = start,
                Length = boundedLength,
                Style = style
            });
        }
    }

    private static MarkdownTextResult ParseMarkdownTextStyles(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new MarkdownTextResult(text, []);

        var output = new StringBuilder();
        var styles = new List<SignalTextStyle>();
        var stack = new Stack<OpenStyle>();

        for (var i = 0; i < text.Length;)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                output.Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (TryMatchDelimiter(text, i, out var delimiter, out var style))
            {
                if (stack.Count > 0 && string.Equals(stack.Peek().Delimiter, delimiter, StringComparison.Ordinal))
                {
                    var open = stack.Pop();
                    var length = output.Length - open.Start;
                    if (length > 0)
                    {
                        styles.Add(new SignalTextStyle
                        {
                            Start = open.Start,
                            Length = length,
                            Style = open.Style
                        });
                    }

                    i += delimiter.Length;
                    continue;
                }

                if (HasClosingDelimiter(text, i + delimiter.Length, delimiter))
                {
                    stack.Push(new OpenStyle(delimiter, style, output.Length));
                    i += delimiter.Length;
                    continue;
                }
            }

            output.Append(text[i]);
            i++;
        }

        return new MarkdownTextResult(output.ToString(), styles);
    }

    private static bool TryMatchDelimiter(string text, int index, out string delimiter, out string style)
    {
        delimiter = string.Empty;
        style = string.Empty;

        if (index + 1 < text.Length)
        {
            var pair = text.AsSpan(index, 2);
            if (pair.SequenceEqual("**"))
            {
                delimiter = "**";
                style = "BOLD";
                return true;
            }

            if (pair.SequenceEqual("~~"))
            {
                delimiter = "~~";
                style = "STRIKETHROUGH";
                return true;
            }

            if (pair.SequenceEqual("||"))
            {
                delimiter = "||";
                style = "SPOILER";
                return true;
            }
        }

        if (text[index] == '`')
        {
            delimiter = "`";
            style = "MONOSPACE";
            return true;
        }

        if (text[index] == '*')
        {
            delimiter = "*";
            style = "ITALIC";
            return true;
        }

        if (text[index] == '_')
        {
            delimiter = "_";
            style = "ITALIC";
            return true;
        }

        return false;
    }

    private static bool HasClosingDelimiter(string text, int searchFrom, string delimiter)
    {
        if (searchFrom >= text.Length)
            return false;

        return text.IndexOf(delimiter, searchFrom, StringComparison.Ordinal) >= 0;
    }

    private static bool TryMapSignalStyle(JsonElement styleItem, out string style)
    {
        style = string.Empty;
        var rawStyle = styleItem.TryGetProperty("style", out var styleElement)
            ? styleElement.GetString()
            : styleItem.TryGetProperty("textStyle", out var textStyleElement)
                ? textStyleElement.GetString()
                : null;

        if (string.IsNullOrWhiteSpace(rawStyle))
            return false;

        style = rawStyle.Trim().ToUpperInvariant() switch
        {
            "BOLD" => "BOLD",
            "ITALIC" => "ITALIC",
            "SPOILER" => "SPOILER",
            "STRIKETHROUGH" => "STRIKETHROUGH",
            "STRIKE-THROUGH" => "STRIKETHROUGH",
            "STRIKE_THROUGH" => "STRIKETHROUGH",
            "MONOSPACE" => "MONOSPACE",
            "CODE" => "MONOSPACE",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(style);
    }

    private static bool TryReadRange(JsonElement styleItem, out int start, out int length)
    {
        start = 0;
        length = 0;

        var hasStart = styleItem.TryGetProperty("start", out var startElement)
            || styleItem.TryGetProperty("offset", out startElement);

        if (!hasStart
            || !startElement.TryGetInt32(out start))
        {
            return false;
        }

        if (styleItem.TryGetProperty("length", out var lengthElement)
            && lengthElement.TryGetInt32(out length))
        {
            return true;
        }

        if (styleItem.TryGetProperty("end", out var endElement)
            && endElement.TryGetInt32(out var end)
            && end > start)
        {
            length = end - start;
            return true;
        }

        if (styleItem.TryGetProperty("stop", out var stopElement)
            && stopElement.TryGetInt32(out var stop)
            && stop > start)
        {
            length = stop - start;
            return true;
        }

        return false;
    }
}

public sealed record MarkdownTextResult(string Text, IReadOnlyList<SignalTextStyle> TextStyles);
public sealed record OpenStyle(string Delimiter, string Style, int Start);

public sealed record GatewayTurnResult(string Text, IReadOnlyList<SignalTextStyle> TextStyles);

public sealed class SignalTextStyle
{
    public int Start { get; set; }
    public int Length { get; set; }
    public string Style { get; set; } = string.Empty;
}

public interface ITransportClient
{
    Task<InboundMessage?> ReceiveAsync(CancellationToken ct);
    Task SendAsync(string account, string recipient, string text, IReadOnlyList<SignalTextStyle> textStyles, CancellationToken ct);
    Task StartTypingAsync(string account, string recipient, CancellationToken ct);
    Task StopTypingAsync(string account, string recipient, CancellationToken ct);
}

public sealed class TypingIndicatorKeepAlive : IAsyncDisposable
{
    private readonly ITransportClient _transport;
    private readonly string _account;
    private readonly string _recipient;
    private readonly ILogger _logger;
    private readonly SignalSettings _settings;
    private readonly PeriodicTimer? _timer;
    private readonly CancellationTokenSource _loopCts;
    private readonly Task? _loopTask;
    private int _stopped;

    private TypingIndicatorKeepAlive(
        ITransportClient transport,
        string account,
        string recipient,
        SignalSettings settings,
        ILogger logger,
        CancellationToken ct)
    {
        _transport = transport;
        _account = account;
        _recipient = recipient;
        _logger = logger;
        _settings = settings;
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (settings.TypingIndicatorEnabled)
        {
            var keepAliveSeconds = Math.Max(1, settings.TypingKeepAliveSeconds);
            _timer = new PeriodicTimer(TimeSpan.FromSeconds(keepAliveSeconds));
            _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token));
        }
    }

    public static TypingIndicatorKeepAlive Start(
        ITransportClient transport,
        string account,
        string recipient,
        SignalSettings settings,
        ILogger logger,
        CancellationToken ct)
    {
        var keepAlive = new TypingIndicatorKeepAlive(transport, account, recipient, settings, logger, ct);
        _ = keepAlive.TryStartTypingAsync(ct);
        return keepAlive;
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        _loopCts.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask;
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation during shutdown.
            }
        }

        if (_settings.TypingIndicatorEnabled)
        {
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _settings.TypingStopTimeoutSeconds)));
            try
            {
                await _transport.StopTypingAsync(_account, _recipient, stopCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Signal typing-indicator stop failed for {Recipient}.", _recipient);
            }
        }

        _timer?.Dispose();
        _loopCts.Dispose();
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task RunLoopAsync(CancellationToken ct)
    {
        if (_timer is null)
            return;

        while (await _timer.WaitForNextTickAsync(ct))
        {
            await TryStartTypingAsync(ct);
        }
    }

    private async Task TryStartTypingAsync(CancellationToken ct)
    {
        try
        {
            await _transport.StartTypingAsync(_account, _recipient, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Signal typing-indicator refresh failed for {Recipient}.", _recipient);
        }
    }
}

public sealed class SocketTransportClient(
    IHttpClientFactory httpClientFactory,
    IOptions<SignalSettings> settings,
    IChannelCredentialProvider credentials,
    ILogger<SocketTransportClient> logger) : ITransportClient
{
    private readonly Queue<InboundMessage> _pending = new();
    private readonly Queue<string> _accounts = new();
    private DateTimeOffset _lastAccountRefreshUtc = DateTimeOffset.MinValue;

    public async Task<InboundMessage?> ReceiveAsync(CancellationToken ct)
    {
        if (_pending.Count > 0)
            return _pending.Dequeue();

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
            return null;

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

    public Task StartTypingAsync(string account, string recipient, CancellationToken ct) =>
        SendTypingIndicatorAsync(account, recipient, stop: false, ct);

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
                break;

            if (result.Count > 0)
                await stream.WriteAsync(buffer.AsMemory(0, result.Count), ct);

            if (result.EndOfMessage)
                break;
        }

        if (stream.Length == 0)
            return null;

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
            return;

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
                return [];

            await using var payload = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(payload, cancellationToken: ct);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var accounts = new List<string>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        accounts.Add(value);

                    continue;
                }

                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("number", out var numberElement))
                {
                    var value = numberElement.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        accounts.Add(value);
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
            return;

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
            text = "[non-text Signal message with attachment metadata]";

        if (string.IsNullOrWhiteSpace(text) && attachments.Count == 0)
            logger.LogDebug("Signal message from {Sender} rejected: message text is empty.", sender);

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
                continue;

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
            return ReadAttachmentIdValue(idElement);

        if (attachment.TryGetProperty("attachmentId", out var attachmentIdElement))
            return ReadAttachmentIdValue(attachmentIdElement);

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
            return raw;

        return $"{raw[..maxChars]}...(truncated)";
    }

    private async Task<IReadOnlyList<InboundAttachment>> EnrichAttachmentsAsync(
        IReadOnlyList<InboundAttachment> attachments,
        CancellationToken ct)
    {
        if (attachments.Count == 0)
            return attachments;

        var maxImagesPerMessage = Math.Max(0, settings.Value.MaxImageAttachmentsPerMessage);
        var maxImageAttachmentBytes = settings.Value.MaxImageAttachmentBytes;

        if (maxImagesPerMessage == 0 || maxImageAttachmentBytes <= 0)
            return attachments;

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
                return string.Empty;

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
                return string.Empty;

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

public interface IChannelCredentialProvider
{
    Task<string> ResolveBearerTokenAsync(string senderId, CancellationToken ct);
}

public sealed class DatabaseChannelCredentialProvider(
    IDbContextFactory<EntityContext> dbContextFactory,
    ILogger<DatabaseChannelCredentialProvider> logger) : IChannelCredentialProvider
{
    public async Task<string> ResolveBearerTokenAsync(string senderId, CancellationToken ct)
    {
        var (token, matchCount) = await ChannelSenderBindingTokenResolver.ResolveAsync(
            dbContextFactory,
            senderId,
            ChannelEntity.SignalName,
            ChannelEntity.SignalName,
            ct);

        if (matchCount > 1)
        {
            logger.LogWarning("Multiple active Signal bindings found for sender {SenderId}; refusing to select a token.", senderId);
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("No Signal JWT token found for sender {SenderId} in ChannelSenderBindings.", senderId);
        }

        return token;
    }
}

public sealed record InboundMessage(
    string Account,
    string Sender,
    string Text,
    string BearerToken,
    IReadOnlyList<InboundAttachment> Attachments);

public sealed record InboundAttachment(string AttachmentId, string ContentType, string FileName, string ImageDataUrl)
{
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
