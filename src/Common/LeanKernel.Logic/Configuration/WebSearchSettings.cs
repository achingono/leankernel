namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Web search backend configuration nested under <c>Agents:Tools:WebSearch</c>.
/// </summary>
public sealed class WebSearchSettings
{
    /// <summary>
    /// Gets or sets the preferred provider: "brave" (default) or "duckduckgo".
    /// </summary>
    public string Provider { get; set; } = "brave";

    /// <summary>
    /// Gets or sets the environment variable name holding the Brave API key.
    /// </summary>
    public string ApiKeyEnv { get; set; } = "BRAVE_API_KEY";

    /// <summary>
    /// Gets or sets the egress allowlist for web search backend hosts.
    /// </summary>
    public IReadOnlyList<string> AllowHosts { get; set; } =
        ["api.search.brave.com", "api.duckduckgo.com"];
}
