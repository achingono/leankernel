namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configuration for HTTP-based embedding requests.
/// </summary>
public sealed class EmbeddingClientSettings
{
    /// <summary>
    /// Gets or sets the full embeddings endpoint URL.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the API key used for endpoint authentication.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the authorization scheme.
    /// </summary>
    public string AuthScheme { get; set; } = Constants.Http.Headers.Bearer;
}