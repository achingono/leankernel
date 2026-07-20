namespace LeanKernel.Channels.Common.Configuration;

/// <summary>Configuration options for connecting to the LeanKernel Gateway.</summary>
public sealed class GatewaySettings
{
    /// <summary>Base URL of the Gateway instance.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5088";

    /// <summary>Default model identifier to use for completions.</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Default agent name to route requests to.</summary>
    public string AgentName { get; set; } = "leankernel";
}