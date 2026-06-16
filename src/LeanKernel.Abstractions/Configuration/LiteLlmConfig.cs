namespace LeanKernel.Abstractions.Configuration;

public sealed class LiteLlmConfig
{
    public string BaseUrl { get; set; } = "http://litellm:4000";
    public string ApiKey { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = "tool";
    public int ContextWindowTokens { get; set; } = 128_000;
    public int MaxTools { get; set; } = 128;
}
