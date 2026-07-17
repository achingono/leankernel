namespace LeanKernel.Channels.Common.Settings;

public sealed class GatewaySettings
{
    public string BaseUrl { get; set; } = "http://localhost:5088";
    public string Model { get; set; } = "gpt-4o-mini";
    public string AgentName { get; set; } = "leankernel";
}
