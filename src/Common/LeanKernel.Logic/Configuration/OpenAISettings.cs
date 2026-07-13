namespace LeanKernel.Logic.Configuration;

public class OpenAISettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = string.Empty;

    public FactExtractionSettings FactExtraction { get; set; } = new FactExtractionSettings();
    public MemorySettings Memory { get; set; } = new MemorySettings();
}