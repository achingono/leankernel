using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Abstraction for messaging channels (Signal, Telegram, Discord, etc.).
/// Each channel adapter implements this interface.
/// </summary>
public interface IChannel : IAsyncDisposable
{
    string ChannelId { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task SendAsync(string recipientId, string content, CancellationToken ct);
    event Func<LeanKernelMessage, CancellationToken, Task> OnMessageReceived;
}
