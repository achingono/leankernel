namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configuration settings for LiteLLM.
/// </summary>
public sealed class LiteLlmConfig
{
    /// <summary>
    /// Gets or sets the base URL for the LiteLLM service.
    /// </summary>
    public string BaseUrl { get; set; } = "http://litellm:4000";

    /// <summary>
    /// Gets or sets the API key for the LiteLLM service.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default model name.
    /// </summary>
    public string DefaultModel { get; set; } = "tool";

    /// <summary>
    /// Gets or sets the context window token size.
    /// </summary>
    public int ContextWindowTokens { get; set; } = 128_000;

    /// <summary>
    /// Gets or sets the maximum number of tools.
    /// </summary>
    public int MaxTools { get; set; } = 128;
}
