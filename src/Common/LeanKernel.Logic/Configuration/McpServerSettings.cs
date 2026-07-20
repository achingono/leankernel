namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configuration for a single MCP server endpoint, nested under <c>Agents:Tools:McpServers</c>.
/// </summary>
public sealed class McpServerSettings
{
    /// <summary>
    /// Gets or sets the unique name of this MCP server.
    /// Used as the tool category and for logging.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the HTTP/SSE endpoint URL of the MCP server.
    /// </summary>
    public required string Endpoint { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this MCP server is enabled.
    /// Disabled servers are skipped during tool discovery.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the transport mode: "AutoDetect" (default), "StreamableHttp", or "Sse".
    /// AutoDetect tries Streamable HTTP first and falls back to SSE.
    /// </summary>
    public string TransportMode { get; set; } = "AutoDetect";

    /// <summary>
    /// Gets or sets the connection timeout in seconds.
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets optional additional HTTP headers to send with MCP requests.
    /// Keys are header names, values are header values.
    /// </summary>
    public IReadOnlyDictionary<string, string> AdditionalHeaders { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or sets a value indicating whether tool discovery failures for this server
    /// should be treated as fatal startup errors. Defaults to false (log warning and continue).
    /// </summary>
    public bool Required { get; set; }
}