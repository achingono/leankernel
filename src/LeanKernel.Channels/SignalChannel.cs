using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels;

public sealed class SignalChannel : IChannel
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

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

    public string ChannelId => "signal";

    public bool IsConnected => Volatile.Read(ref _connectedCount) > 0;

    public event Func<ChannelMessage, Task>? MessageReceived;

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

    public async Task SendAsync(string recipientId, string message, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var numbers = _config.GetPhoneNumbers();
        if (numbers.Count == 0)
        {
            throw new InvalidOperationException("No phone numbers configured for Signal channel");
        }

        var sourceNumber = numbers[0];
        if (_senderNumber.TryGetValue(recipientId, out var mapped))
        {
            sourceNumber = mapped;
        }

        var client = _httpClientFactory.CreateClient("signal-daemon");
        using var response = await client.PostAsJsonAsync(
            "/v2/send",
            new SignalSendRequest
            {
                Number = sourceNumber,
                Recipients = [recipientId],
                Message = message
            },
            SerializerOptions,
            ct).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

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
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(wsUri, ct).ConfigureAwait(false);

                Interlocked.Increment(ref _connectedCount);
                wasEverConnected = true;
                reconnectAttempts = 0;
                _logger.LogInformation("Connected to Signal daemon for {PhoneNumber}", phoneNumber);

                var buffer = new byte[8192];
                var messageBuffer = new StringBuilder();

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None)
                            .ConfigureAwait(false);
                        break;
                    }

                    messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var json = messageBuffer.ToString();
                        messageBuffer.Clear();

                        var envelope = JsonSerializer.Deserialize<SignalReceiveEnvelope>(json, SerializerOptions);
                        var channelMessage = CreateChannelMessage(envelope, phoneNumber);
                        if (channelMessage is not null)
                        {
                            _senderNumber[channelMessage.SenderId] = phoneNumber;
                            await DispatchMessageAsync(channelMessage, ct).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                reconnectAttempts++;

                if (wasEverConnected)
                {
                    Interlocked.Decrement(ref _connectedCount);
                    wasEverConnected = false;
                }

                _logger.LogWarning(
                    ex,
                    "Signal WebSocket disconnected for {PhoneNumber} (attempt {Attempt}/{MaxAttempts})",
                    phoneNumber,
                    reconnectAttempts,
                    _config.MaxReconnectAttempts);

                if (_config.MaxReconnectAttempts > 0 && reconnectAttempts >= _config.MaxReconnectAttempts)
                {
                    _logger.LogError(
                        "Signal channel reached the maximum reconnect attempts for {PhoneNumber} and will stop",
                        phoneNumber);
                    break;
                }

                var reconnectDelay = GetReconnectDelay(reconnectAttempts);
                if (reconnectDelay > TimeSpan.Zero)
                {
                    await Task.Delay(reconnectDelay, ct).ConfigureAwait(false);
                }
            }
        }

        if (wasEverConnected)
        {
            Interlocked.Decrement(ref _connectedCount);
        }
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

    private static ChannelMessage? CreateChannelMessage(SignalReceiveEnvelope? item, string recipientId)
    {
        if (item is null) return null;

        var envelope = item.Envelope;
        var senderId = envelope?.SourceNumber ?? envelope?.Source;
        var content = envelope?.DataMessage?.Message;

        if (string.IsNullOrWhiteSpace(senderId) || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        return new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = senderId,
            RecipientId = recipientId,
            Content = content,
            Timestamp = envelope?.Timestamp is > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(envelope.Timestamp.Value)
                : DateTimeOffset.UtcNow
        };
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
}

internal sealed class SignalReceiveEnvelope
{
    [JsonPropertyName("envelope")]
    public SignalEnvelope? Envelope { get; set; }

    [JsonPropertyName("account")]
    public string? Account { get; set; }
}

internal sealed class SignalEnvelope
{
    [JsonPropertyName("sourceNumber")]
    public string? SourceNumber { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("timestamp")]
    public long? Timestamp { get; set; }

    [JsonPropertyName("dataMessage")]
    public SignalDataMessage? DataMessage { get; set; }
}

internal sealed class SignalDataMessage
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class SignalSendRequest
{
    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("recipients")]
    public List<string> Recipients { get; set; } = [];

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
