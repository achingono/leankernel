namespace LeanKernel.Services.Gateway.Configuration;

/// <summary>
/// Represents the settings used for configuring gateway hardening parameters.
/// </summary>
public sealed class HardeningSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether an API key is required for requests to the gateway.
    /// If set to true, requests must include a valid API key in the specified header.
    /// If set to false, requests can be made without an API key.
    /// </summary>
    public bool RequireApiKey { get; set; }

    /// <summary>
    /// Gets or sets the name of the HTTP header that clients must use to provide the API key.
    /// </summary>
    public string ApiKeyHeader { get; set; } = "X-Api-Key";

    /// <summary>
    /// Gets or sets the API key that clients must provide in the specified header to access the gateway.
    /// This value should be kept secret and only shared with authorized clients.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the HTTP header that clients must use to provide a correlation ID for request tracing.
    /// </summary>
    public string CorrelationIdHeader { get; set; } = "X-Correlation-ID";
}