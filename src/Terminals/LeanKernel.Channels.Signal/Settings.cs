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
    /// Gets or sets the client-side receive deadline in seconds.
    /// </summary>
    public int ReceiveClientDeadlineSeconds { get; set; } = 25;

    /// <summary>
    /// Gets or sets the delay in seconds before reconnecting after a WebSocket failure.
    /// </summary>
    public int ReconnectDelaySeconds { get; set; } = 1;

    /// <summary>
    /// Gets or sets the account refresh interval in seconds.
    /// </summary>
    public int AccountRefreshSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the max number of pending inbound messages kept in memory.
    /// </summary>
    public int InboundQueueCapacity { get; set; } = 100;

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

    /// <summary>
    /// Gets or sets a value indicating whether the socket worker health check is enabled.
    /// </summary>
    public bool EnableWorkerHealthCheck { get; set; } = true;

    /// <summary>
    /// Gets or sets the progress stall threshold in seconds before a worker is reported as Degraded.
    /// </summary>
    public int WorkerDegradedThresholdSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the progress stall threshold in seconds before a worker is reported as Unhealthy.
    /// </summary>
    public int WorkerUnhealthyThresholdSeconds { get; set; } = 180;

    /// <summary>
    /// Gets or sets the consecutive loop error limit before a worker is reported as Degraded.
    /// </summary>
    public int WorkerConsecutiveErrorThreshold { get; set; } = 3;

    /// <summary>
    /// Gets or sets the consecutive loop error limit before a worker transitions to Faulted and is reported as Unhealthy.
    /// </summary>
    public int WorkerUnhealthyErrorThreshold { get; set; } = 10;
}