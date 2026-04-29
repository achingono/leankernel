using Microsoft.Extensions.Logging;
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

    private readonly string _cliPath;
    private readonly string _account;
    private readonly ILogger<SignalChannel> _logger;

    public event Func<LeanKernelMessage, CancellationToken, Task>? OnMessageReceived;

    public SignalChannel(string cliPath, string account, ILogger<SignalChannel> logger)
    {
        _cliPath = cliPath;
        _account = account;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        // TODO: Phase 3 — Start signal-cli JSON-RPC process and listen for messages
        _logger.LogInformation("Signal channel starting (account: {Account})", _account);
        _logger.LogWarning("Signal channel is a stub — Phase 3 implementation pending");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Signal channel stopping");
        return Task.CompletedTask;
    }

    public Task SendAsync(string recipientId, string content, CancellationToken ct)
    {
        // TODO: Phase 3 — Send via signal-cli
        _logger.LogInformation("Signal send to {Recipient}: {Content}",
            recipientId, content.Length > 80 ? content[..80] + "..." : content);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _logger.LogDebug("Signal channel disposed");
        return ValueTask.CompletedTask;
    }
}
