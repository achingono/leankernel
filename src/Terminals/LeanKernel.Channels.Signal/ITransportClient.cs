namespace LeanKernel.Channels.Signal;

/// <summary>
/// Abstraction for sending and receiving messages over a channel transport.
/// </summary>
public interface ITransportClient
{
    /// <summary>
    /// Receives the next inbound message from the transport.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The next inbound message, or <c>null</c> if no message is available.</returns>
    Task<InboundMessage?> ReceiveAsync(CancellationToken ct);

    /// <summary>
    /// Sends a text message with optional text styles to a recipient.
    /// </summary>
    /// <param name="account">The sending account identifier.</param>
    /// <param name="recipient">The recipient identifier.</param>
    /// <param name="text">The message text.</param>
    /// <param name="textStyles">The text styles to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SendAsync(string account, string recipient, string text, IReadOnlyList<SignalTextStyle> textStyles, CancellationToken ct);

    /// <summary>
    /// Sends a typing indicator start notification.
    /// </summary>
    /// <param name="account">The sending account identifier.</param>
    /// <param name="recipient">The recipient identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StartTypingAsync(string account, string recipient, CancellationToken ct);

    /// <summary>
    /// Sends a typing indicator stop notification.
    /// </summary>
    /// <param name="account">The sending account identifier.</param>
    /// <param name="recipient">The recipient identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StopTypingAsync(string account, string recipient, CancellationToken ct);
}