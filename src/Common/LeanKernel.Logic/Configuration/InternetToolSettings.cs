namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Internet tool configuration nested under <c>Agents:Tools:Internet</c>.
/// </summary>
public sealed class InternetToolSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether internet tools (web_fetch, http_request) are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum redirect hops for web_fetch and http_request.
    /// </summary>
    public int MaxRedirects { get; set; } = 3;
}
