namespace LeanKernel.Channels.Common.Settings;

/// <summary>
/// Settings for connecting to the LeanKernel Gateway from a channel terminal.
/// </summary>
public sealed class GatewaySettings
{
    /// <summary>
    /// Base URL of the gateway.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5088";

    /// <summary>
    /// The default model to use for gateway requests.
    /// </summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// The default agent name to use for gateway requests.
    /// </summary>
    public string AgentName { get; set; } = "leankernel";
}