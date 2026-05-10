using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Abstraction for messaging channels (Signal, Telegram, Discord, etc.).
/// Each channel adapter implements this interface.
/// </summary>
public interface IChannel : IAsyncDisposable
{
    /// <summary>
    /// Gets the stable channel identifier.
    /// </summary>
    string ChannelId { get; }

    /// <summary>
    /// Determines whether the sender is allowed to interact through this channel.
    /// </summary>
    /// <param name="senderId">The sender identifier from the channel.</param>
    /// <returns><see langword="true" /> when the sender is authorized; otherwise <see langword="false" />.</returns>
    bool IsAuthorizedSender(string senderId);

    /// <summary>
    /// Starts receiving messages from the channel.
    /// </summary>
    /// <param name="ct">A token used to cancel startup.</param>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Stops receiving messages from the channel.
    /// </summary>
    /// <param name="ct">A token used to cancel shutdown.</param>
    Task StopAsync(CancellationToken ct);

    /// <summary>
    /// Sends a response to a channel recipient.
    /// </summary>
    /// <param name="recipientId">The recipient identifier for the channel.</param>
    /// <param name="content">The response content to send.</param>
    /// <param name="ct">A token used to cancel sending.</param>
    Task SendAsync(string recipientId, string content, CancellationToken ct);

    /// <summary>
    /// Raised when a message is received from the channel.
    /// </summary>
    event Func<LeanKernelMessage, CancellationToken, Task> OnMessageReceived;
}
