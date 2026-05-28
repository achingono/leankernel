namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configures LeanKernel's optional browser automation sidecar service.
/// </summary>
public sealed class BrowserServiceConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether browser tools and sidecar integration are enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the browser-service root URL.
    /// </summary>
    public string BaseUrl { get; set; } = Uri.UriSchemeHttp + "://browser-service:8000";

    /// <summary>
    /// Gets or sets the bearer token used for browser-service operational endpoints.
    /// </summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the browser-service HTTP request timeout in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Gets or sets the maximum artifact bytes returned by the browser artifact tool.
    /// </summary>
    public int MaxArtifactBytes { get; set; } = 2_000_000;

    /// <summary>
    /// Gets or sets the maximum response characters emitted by browser tools.
    /// </summary>
    public int MaxOutputChars { get; set; } = 12_000;

    /// <summary>
    /// Gets or sets the default LiteLLM model alias used by browser tasks.
    /// </summary>
    public string DefaultModel { get; set; } = "gpt-4o";

    /// <summary>
    /// Gets or sets browser-service health probe configuration.
    /// </summary>
    public BrowserServiceHealthProbeConfig HealthProbe { get; set; } = new();
}

/// <summary>
/// Configures health tracking for the browser automation sidecar.
/// </summary>
public sealed class BrowserServiceHealthProbeConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether authenticated browser-service readiness probing is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
