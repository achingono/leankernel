namespace LeanKernel.Channels.Signal;

/// <summary>
/// Configuration settings for the Signal channel transport.
/// </summary>
public sealed class SignalSettings
{
    /// <summary>
    /// Gets or sets the path to the signal-cli UNIX socket (unused when Host/Port are set).
    /// </summary>
    public string SocketPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the signal-cli REST API hostname.
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// Gets or sets the signal-cli REST API port.
    /// </summary>
    public int Port { get; set; } = 8080;

    /// <summary>
    /// Gets or sets the WebSocket receive timeout in seconds.
    /// </summary>
    public int ReceiveTimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Gets or sets the delay in seconds before reconnecting after a WebSocket failure.
    /// </summary>
    public int ReconnectDelaySeconds { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum number of image attachments to forward per message.
    /// </summary>
    public int MaxImageAttachmentsPerMessage { get; set; } = 3;

    /// <summary>
    /// Gets or sets the maximum size in bytes for downloaded image attachments.
    /// </summary>
    public int MaxImageAttachmentBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Gets or sets whether typing indicators are enabled.
    /// </summary>
    public bool TypingIndicatorEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the interval in seconds between typing indicator keep-alive requests.
    /// </summary>
    public int TypingKeepAliveSeconds { get; set; } = 7;

    /// <summary>
    /// Gets or sets the timeout in seconds for the typing indicator stop request.
    /// </summary>
    public int TypingStopTimeoutSeconds { get; set; } = 2;

    /// <summary>
    /// Gets or sets the timeout in seconds for individual typing indicator requests.
    /// </summary>
    public int TypingRequestTimeoutSeconds { get; set; } = 3;
}