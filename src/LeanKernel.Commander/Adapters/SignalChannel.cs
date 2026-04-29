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
    private readonly ILogger<SignalChannel> _logger;
    private SignalCliAdapter? _adapter;

    public event Func<LeanKernelMessage, CancellationToken, Task>? OnMessageReceived;

    public SignalChannel(IOptions<LeanKernelConfig> config, ILogger<SignalChannel> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_config.Signal.Enabled)
        {
            _logger.LogInformation("Signal channel disabled in configuration");
            return;
        }

        _adapter = new SignalCliAdapter(
            _config.Signal.CliPath,
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
}
