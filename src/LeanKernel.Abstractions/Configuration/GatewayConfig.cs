namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configuration settings for Gateway API authentication.
/// </summary>
public sealed class GatewayConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether protected gateway endpoints require an API key.
    /// </summary>
    public bool RequireApiKey { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether protected gateway endpoints can be called anonymously when no key is configured.
    /// </summary>
    public bool AllowAnonymous { get; set; } = false;

    /// <summary>
    /// Gets or sets a single API key for protected endpoints.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets API keys for protected endpoints.
    /// </summary>
    public List<string> ApiKeys { get; set; } = [];

    /// <summary>
    /// Gets or sets a single API key for admin-only endpoints.
    /// </summary>
    public string? AdminApiKey { get; set; }

    /// <summary>
    /// Gets or sets API keys for admin-only endpoints.
    /// </summary>
    public List<string> AdminApiKeys { get; set; } = [];
}
