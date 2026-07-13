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
}
