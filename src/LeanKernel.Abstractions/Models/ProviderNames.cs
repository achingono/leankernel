namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Well-known provider names used by health tracking and graceful degradation.
/// </summary>
public static class ProviderNames
{
    /// <summary>
    /// The PostgreSQL database provider name.
    /// </summary>
    public const string Database = "database";

    /// <summary>
    /// The LiteLLM provider name.
    /// </summary>
    public const string LiteLlm = "litellm";

    /// <summary>
    /// The GBrain provider name.
    /// </summary>
    public const string GBrain = "gbrain";

    /// <summary>
    /// The browser automation sidecar provider name.
    /// </summary>
    public const string BrowserService = "browser-service";
}
