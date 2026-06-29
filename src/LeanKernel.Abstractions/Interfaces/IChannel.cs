using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Defines the interface for a communication channel.
/// </summary>
public interface IChannel
{
    /// <summary>
    /// Gets the unique identifier for the channel.
    /// </summary>
    string ChannelId { get; }

    /// <summary>
    /// Gets a value indicating whether the channel is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Starts the channel.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops the channel.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Notifies the recipient that the sender is typing.
    /// </summary>
    /// <param name="recipientId">The recipient identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StartTypingAsync(string recipientId, CancellationToken ct = default);

    /// <summary>
    /// Notifies the recipient that the sender has stopped typing.
    /// </summary>
    /// <param name="recipientId">The recipient identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StopTypingAsync(string recipientId, CancellationToken ct = default);

    /// <summary>
    /// Sends a message to the specified recipient via the channel.
    /// </summary>
    /// <param name="recipientId">The recipient identifier.</param>
    /// <param name="message">The message content.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SendAsync(string recipientId, string message, CancellationToken ct = default);

    /// <summary>
    /// Occurs when a message is received on the channel.
    /// </summary>
    event Func<ChannelMessage, Task>? MessageReceived;
}

/// <summary>
/// Represents a message sent through a channel.
/// </summary>
public sealed record ChannelMessage
{
    /// <summary>
    /// Gets the identifier of the channel.
    /// </summary>
    public required string ChannelId { get; init; }

    /// <summary>
    /// Gets the identifier of the sender.
    /// </summary>
    public required string SenderId { get; init; }

    /// <summary>
    /// Gets the identifier of the recipient.
    /// </summary>
    public string? RecipientId { get; init; }

    /// <summary>
    /// Gets the content of the message.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets the timestamp when the message was sent.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the list of attachments in the message.
    /// </summary>
    public IReadOnlyList<Attachment>? Attachments { get; init; }
}
