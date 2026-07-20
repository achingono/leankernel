namespace LeanKernel.Gateway.Configuration;

/// <summary>
/// Configures the LeanKernel connection to the external GBrain MCP service.
/// </summary>
public sealed class GBrainSettings
{
    /// <summary>
    /// Gets or sets the root HTTP endpoint exposed by the GBrain MCP server.
    /// </summary>
    public string BaseUrl { get; set; } = "http://gbrain:8789";

    /// <summary>
    /// Gets or sets the optional bearer token used for GBrain requests.
    /// </summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the per-request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}