namespace LeanKernel.Gateway.Channels;

/// <summary>
/// Defines the interface for a communication channel adapter.
/// </summary>
public interface IChannel
{
    /// <summary>
    /// Gets the unique identifier for this channel (e.g., "signal", "openai-http").
    /// </summary>
    string ChannelId { get; }

    /// <summary>
    /// Gets a value indicating whether the channel is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Starts the channel's receive loop.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops the channel's receive loop.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends a message to a recipient via this channel.
    /// </summary>
    /// <param name="recipientId">The recipient identifier.</param>
    /// <param name="message">The message content.</param>
    /// <param name="attachments">Optional attachments.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendAsync(string recipientId, string message, IReadOnlyList<Attachment>? attachments = null, CancellationToken ct = default);

    /// <summary>
    /// Signals that the sender is typing to the specified recipient.
    /// </summary>
    /// <param name="recipientId">The recipient identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StartTypingAsync(string recipientId, CancellationToken ct = default);

    /// <summary>
    /// Signals that the sender has stopped typing to the specified recipient.
    /// </summary>
    /// <param name="recipientId">The recipient identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StopTypingAsync(string recipientId, CancellationToken ct = default);

    /// <summary>
    /// Occurs when a message is received on this channel.
    /// </summary>
    event Func<ChannelMessage, Task>? MessageReceived;
}
