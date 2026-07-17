namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configures the default agent registration and prompt metadata.
/// </summary>
public class AgentSettings
{
    /// <summary>
    /// Gets or sets the root folder used to locate agent definitions.
    /// </summary>
    public string RootPath { get; set; } = "agents";

    /// <summary>
    /// Gets or sets the default agent name.
    /// </summary>
    public string DefaultName { get; set; } = "leankernel";

    /// <summary>
    /// Gets or sets the default instructions applied to registered agents.
    /// </summary>
    public string DefaultInstructions { get; set; } = "You are a helpful AI assistant.";

    /// <summary>
    /// Gets or sets the default agent description.
    /// </summary>
    public string DefaultDescription { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tool runtime configuration nested under <c>Agents:Tools</c>.
    /// </summary>
    public ToolSettings Tools { get; set; } = new ToolSettings();

    /// <summary>
    /// Gets or sets the channel terminal and policy configuration nested under <c>Agents:Channels</c>.
    /// </summary>
    public ChannelSettings Channels { get; set; } = new ChannelSettings();

    /// <summary>
    /// Gets or sets the model telemetry configuration nested under <c>Agents:Telemetry</c>.
    /// </summary>
    public TelemetrySettings Telemetry { get; set; } = new TelemetrySettings();
}
