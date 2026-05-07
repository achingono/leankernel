using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Commander.Adapters;

/// <summary>
/// Signal channel — receives inbound messages and sends replies via Signal.
/// Uses <see cref="SignalRestApiAdapter"/> when <c>Signal.DaemonBaseUrl</c> is configured,
/// otherwise falls back to the local <see cref="SignalCliAdapter"/> child-process mode.
/// </summary>
public sealed class SignalChannel : IChannel, ITypingIndicatorChannel
{
    public string ChannelId => "signal";

    private readonly LeanKernelConfig _config;
    private readonly string _cliPath;
    private readonly ILogger<SignalChannel> _logger;
    private readonly IAttachmentTextExtractionService _attachmentTextExtractor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HashSet<string> _allowedSenders;
    private ISignalAdapter? _adapter;

    public event Func<LeanKernelMessage, CancellationToken, Task>? OnMessageReceived;

    public SignalChannel(
        IOptions<LeanKernelConfig> config,
        ILogger<SignalChannel> logger,
        IAttachmentTextExtractionService attachmentTextExtractor,
        IHttpClientFactory httpClientFactory)
    {
        _config = config.Value;
        _logger = logger;
        _attachmentTextExtractor = attachmentTextExtractor;
        _httpClientFactory = httpClientFactory;
        _cliPath = ResolveSignalCliPath(_config.Signal.CliPath) ?? _config.Signal.CliPath;
        _allowedSenders = (_config.Signal.AllowedSenders ?? [])
            .Where(sender => !string.IsNullOrWhiteSpace(sender))
            .Select(sender => sender.Trim())
            .ToHashSet(StringComparer.Ordinal);
    }

    public bool IsAuthorizedSender(string senderId)
    {
        if (_allowedSenders.Count == 0)
            return true;

        return _allowedSenders.Contains(senderId);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_config.Signal.Enabled)
        {
            _logger.LogInformation("Signal channel disabled in configuration");
            return;
        }

        if (!string.IsNullOrWhiteSpace(_config.Signal.DaemonBaseUrl))
        {
            var http = _httpClientFactory.CreateClient("signal-daemon");
            _adapter = new SignalRestApiAdapter(
                _config.Signal.DaemonBaseUrl,
                _config.Signal.Account,
                http,
                _logger,
                _attachmentTextExtractor);
            _logger.LogInformation("Signal channel using HTTP daemon at {DaemonUrl}", _config.Signal.DaemonBaseUrl);
        }
        else
        {
            _adapter = new SignalCliAdapter(
                _cliPath,
                _config.Signal.Account,
                _logger,
                _attachmentTextExtractor);
            _logger.LogInformation("Signal channel using local signal-cli process");
        }

        _adapter.OnMessage += msg =>
        {
            var content = InboundMessageContentFormatter.FormatContent(msg.Body, msg.Attachments);
            var metadata = InboundMessageContentFormatter.BuildMetadata("signal", msg.Attachments);
            var normalized = MessageNormalizer.Normalize(
                channelId: "signal",
                senderId: msg.Sender,
                rawContent: content,
                metadata: metadata);

            _ = Task.Run(async () =>
            {
                try
                {
                    if (OnMessageReceived is not null)
                        await OnMessageReceived(normalized, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling Signal message from {Sender}", msg.Sender);
                }
            }, ct);
        };

        _adapter.OnError += error =>
            _logger.LogWarning("Signal adapter error: {Error}", error);

        try
        {
            await _adapter.StartAsync(ct);
            _logger.LogInformation("Signal channel started (account: {Account})", _config.Signal.Account);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Signal channel — running in degraded mode");
        }
    }

    public Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Signal channel stopping");
        return Task.CompletedTask;
    }

    public async Task SendAsync(string recipientId, string content, CancellationToken ct)
    {
        if (_adapter is null)
        {
            _logger.LogWarning("Signal adapter not initialized — message not sent");
            return;
        }

        await _adapter.SendMessageAsync(recipientId, content, ct);
        _logger.LogDebug("Signal message sent to {Recipient}", recipientId);
    }

    public async ValueTask<IAsyncDisposable> BeginTypingAsync(string recipientId, CancellationToken ct)
    {
        if (_adapter is null || string.IsNullOrWhiteSpace(recipientId))
            return NoopAsyncDisposable.Instance;

        try
        {
            return await SignalTypingScope.StartAsync(_adapter, recipientId, _logger, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start Signal typing indicator for {Recipient}", recipientId);
            return NoopAsyncDisposable.Instance;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_adapter is not null)
            await _adapter.DisposeAsync();
        _logger.LogDebug("Signal channel disposed");
    }

    private static string? ResolveSignalCliPath(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        foreach (var candidate in new[] { "/usr/bin/signal-cli", "/usr/local/bin/signal-cli" })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private sealed class SignalTypingScope : IAsyncDisposable
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

        private readonly ISignalAdapter _adapter;
        private readonly string _recipientId;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts;
        private Task? _loop;
        private bool _disposed;

        private SignalTypingScope(
            ISignalAdapter adapter,
            string recipientId,
            ILogger logger,
            CancellationTokenSource cts)
        {
            _adapter = adapter;
            _recipientId = recipientId;
            _logger = logger;
            _cts = cts;
        }

        public static async Task<IAsyncDisposable> StartAsync(
            ISignalAdapter adapter,
            string recipientId,
            ILogger logger,
            CancellationToken ct)
        {
            await adapter.SendTypingAsync(recipientId, stop: false, ct);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var scope = new SignalTypingScope(adapter, recipientId, logger, cts);
            scope._loop = scope.RunAsync();
            return scope;
        }

        private async Task RunAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    await Task.Delay(RefreshInterval, _cts.Token);
                    if (_cts.IsCancellationRequested)
                        break;

                    await _adapter.SendTypingAsync(_recipientId, stop: false, _cts.Token);
                }
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                _logger.LogDebug("Signal typing refresh cancelled for {Recipient}", _recipientId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Signal typing refresh failed for {Recipient}", _recipientId);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cts.Cancel();

            try
            {
                if (_loop is not null)
                    await _loop;
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                _logger.LogDebug("Signal typing loop cancelled for {Recipient}", _recipientId);
            }

            try
            {
                using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _adapter.SendTypingAsync(_recipientId, stop: true, stopCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop Signal typing indicator for {Recipient}", _recipientId);
            }
            finally
            {
                _cts.Dispose();
            }
        }
    }
}
