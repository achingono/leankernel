namespace LeanKernel.Gateway.Configuration;

/// <summary>
/// Represents the settings required for OpenID Connect authentication.
/// </summary>
public class OpenIdSettings
{
    /// <summary>
    /// Gets or sets the OpenID Connect authority URL.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OpenID Connect client ID.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OpenID Connect client secret.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether HTTPS is required for metadata retrieval.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Optional explicit metadata address (authority discovery). Use to point to internal HTTP endpoint in dev.
    /// </summary>
    public string? MetadataAddress { get; set; }

    /// <summary>
    /// Optional public origin used to build browser-facing redirects (e.g., http://localhost:8080).
    /// Useful when the authority is reachable internally via a different host (docker compose).
    /// </summary>
    public string? PublicOrigin { get; set; }
}