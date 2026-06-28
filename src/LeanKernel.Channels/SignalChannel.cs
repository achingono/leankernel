using System.Net.Http.Json;
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
                    Task.Run(() => PollLoopAsync(captured, _pollingCts.Token), CancellationToken.None));
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

        var client = _httpClientFactory.CreateClient("signal-daemon");
        using var response = await client.PostAsJsonAsync(
            "/v2/send",
            new SignalSendRequest
            {
                Number = numbers[0],
                Recipients = [recipientId],
                Message = message
            },
            SerializerOptions,
            ct).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

    private async Task PollLoopAsync(string phoneNumber, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("signal-daemon");
        var reconnectAttempts = 0;
        var wasEverConnected = false;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var requestUri = $"/v1/receive/{Uri.EscapeDataString(phoneNumber)}?timeout={Math.Max(0, _config.PollIntervalSeconds)}";
                using var response = await client.GetAsync(requestUri, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var payload = await response.Content.ReadFromJsonAsync<List<SignalReceiveEnvelope>>(SerializerOptions, ct).ConfigureAwait(false)
                    ?? [];

                if (!wasEverConnected)
                {
                    Interlocked.Increment(ref _connectedCount);
                    wasEverConnected = true;
                    _logger.LogInformation("Connected to Signal daemon for {PhoneNumber}", phoneNumber);
                }

                reconnectAttempts = 0;

                foreach (var item in payload)
                {
                    var channelMessage = CreateChannelMessage(item);
                    if (channelMessage is not null)
                    {
                        await DispatchMessageAsync(channelMessage, ct).ConfigureAwait(false);
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
                    "Signal polling failed for {PhoneNumber} (attempt {Attempt}/{MaxAttempts})",
                    phoneNumber,
                    reconnectAttempts,
                    _config.MaxReconnectAttempts);

                if (_config.MaxReconnectAttempts > 0 && reconnectAttempts >= _config.MaxReconnectAttempts)
                {
                    _logger.LogError(
                        "Signal channel reached the maximum reconnect attempts for {PhoneNumber} and will stop polling",
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

    private ChannelMessage? CreateChannelMessage(SignalReceiveEnvelope item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var envelope = item.Envelope;
        var senderId = envelope?.SourceNumber ?? envelope?.Source;
        var content = envelope?.DataMessage?.Message;

        if (string.IsNullOrWhiteSpace(senderId) || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        return new ChannelMessage
        {
            ChannelId = ChannelId,
            SenderId = senderId,
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
