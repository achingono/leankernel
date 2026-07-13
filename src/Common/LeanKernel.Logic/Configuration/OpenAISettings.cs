namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configures the OpenAI-compatible endpoint and default model settings.
/// </summary>
public class OpenAISettings
{
    /// <summary>
    /// Gets or sets the API key used to authenticate with the OpenAI-compatible endpoint.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base URL of the OpenAI-compatible endpoint.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default chat model identifier.
    /// </summary>
    public string DefaultModel { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the fact extraction model settings.
    /// </summary>
    public FactExtractionSettings FactExtraction { get; set; } = new FactExtractionSettings();

    /// <summary>
    /// Gets or sets the memory reasoning settings.
    /// </summary>
    public MemorySettings Memory { get; set; } = new MemorySettings();
}
