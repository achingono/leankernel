namespace LeanKernel.Channels.Signal;

public sealed class GatewaySettings
{
    public string BaseUrl { get; set; } = "http://localhost:5088";
    public string Model { get; set; } = "gpt-4o-mini";
    public string AgentName { get; set; } = "leankernel";
}

public sealed class SignalSettings
{
    public string SocketPath { get; set; } = string.Empty;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8080;
    public int ReceiveTimeoutSeconds { get; set; } = 20;
    public int ReconnectDelaySeconds { get; set; } = 1;
    public int MaxImageAttachmentsPerMessage { get; set; } = 3;
    public int MaxImageAttachmentBytes { get; set; } = 5 * 1024 * 1024;
}
