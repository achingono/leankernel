namespace LeanKernel.Channels.Signal;

public sealed class SignalSettings
{
    public string SocketPath { get; set; } = string.Empty;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8080;
    public int ReceiveTimeoutSeconds { get; set; } = 20;
    public int ReconnectDelaySeconds { get; set; } = 1;
    public int MaxImageAttachmentsPerMessage { get; set; } = 3;
    public int MaxImageAttachmentBytes { get; set; } = 5 * 1024 * 1024;
    public bool TypingIndicatorEnabled { get; set; } = true;
    public int TypingKeepAliveSeconds { get; set; } = 7;
    public int TypingStopTimeoutSeconds { get; set; } = 2;
    public int TypingRequestTimeoutSeconds { get; set; } = 3;
}