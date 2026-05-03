using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Commander.Adapters;

/// <summary>
/// signal-cli adapter using JSON-RPC mode.
/// Manages the signal-cli process and translates Signal messages to/from LeanKernelMessage.
/// </summary>
public sealed class SignalChannel : IChannel
{
    public string ChannelId => "signal";

    private readonly LeanKernelConfig _config;
    private readonly string _cliPath;
    private readonly ILogger<SignalChannel> _logger;
    private readonly HashSet<string> _allowedSenders;
    private SignalCliAdapter? _adapter;

    public event Func<LeanKernelMessage, CancellationToken, Task>? OnMessageReceived;

    public SignalChannel(IOptions<LeanKernelConfig> config, ILogger<SignalChannel> logger)
    {
        _config = config.Value;
        _logger = logger;
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

        _adapter = new SignalCliAdapter(
            _cliPath,
            _config.Signal.Account,
            _logger);

        _adapter.OnMessage += msg =>
        {
            var normalized = MessageNormalizer.Normalize(
                channelId: "signal",
                senderId: msg.Sender,
                rawContent: msg.Body);

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
}
