namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configuration for the LeanKernel tool runtime, nested under <c>Agents:Tools</c>.
/// </summary>
public sealed class ToolSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the tool runtime is enabled.
    /// Setting this to false restores the no-tool chat path.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the web search configuration.
    /// </summary>
    public WebSearchSettings WebSearch { get; set; } = new();

    /// <summary>
    /// Gets or sets the name allowlist. When non-empty, only named tools are exposed.
    /// </summary>
    public IReadOnlyList<string> AllowedToolNames { get; set; } = [];

    /// <summary>
    /// Gets or sets the category allowlist. Applied when <see cref="AllowedToolNames"/> is empty.
    /// </summary>
    public IReadOnlyList<string> AllowedCategories { get; set; } = [];

    /// <summary>
    /// Gets or sets the directories scanned for SKILL.md files at startup.
    /// </summary>
    public IReadOnlyList<string> SkillBasePaths { get; set; } = ["/app/data/skills"];

    /// <summary>
    /// Gets or sets the global HTTP egress ceiling for dynamic HTTP tools.
    /// When non-empty, per-skill <c>egress.allowHosts</c> is intersected with this list.
    /// </summary>
    public DynamicHttpSettings DynamicHttp { get; set; } = new();

    /// <summary>
    /// Gets or sets the built-in calculation and aggregation tool configuration.
    /// </summary>
    public BuiltInCalculationSettings BuiltIns { get; set; } = new();

    /// <summary>
    /// Gets or sets the database query tool configuration.
    /// </summary>
    public DatabaseQuerySettings DatabaseQuery { get; set; } = new();

    /// <summary>
    /// Gets or sets the pre-configured MCP server endpoints.
    /// Tools from enabled servers are discovered at startup and registered in the tool registry.
    /// </summary>
    public IReadOnlyList<McpServerSettings> McpServers { get; set; } = [];

    /// <summary>
    /// Gets or sets the filesystem tool configuration.
    /// </summary>
    public FileSystemToolSettings FileSystem { get; set; } = new();

    /// <summary>
    /// Gets or sets the internet tool configuration.
    /// </summary>
    public InternetToolSettings Internet { get; set; } = new();

    /// <summary>
    /// Gets or sets the document ingestion configuration.
    /// </summary>
    public DocumentIngestionToolSettings DocumentIngestion { get; set; } = new();
}

/// <summary>
/// Web search backend configuration nested under <c>Agents:Tools:WebSearch</c>.
/// </summary>
public sealed class WebSearchSettings
{
    /// <summary>
    /// Gets or sets the preferred provider: "brave" (default) or "duckduckgo".
    /// </summary>
    public string Provider { get; set; } = "brave";

    /// <summary>
    /// Gets or sets the environment variable name holding the Brave API key.
    /// </summary>
    public string ApiKeyEnv { get; set; } = "BRAVE_API_KEY";

    /// <summary>
    /// Gets or sets the egress allowlist for web search backend hosts.
    /// </summary>
    public IReadOnlyList<string> AllowHosts { get; set; } =
        ["api.search.brave.com", "api.duckduckgo.com"];
}

/// <summary>
/// Global HTTP egress settings for dynamic skill tools, nested under <c>Agents:Tools:DynamicHttp</c>.
/// </summary>
public sealed class DynamicHttpSettings
{
    /// <summary>
    /// Gets or sets the global host allowlist ceiling for dynamic HTTP tool egress.
    /// An empty list means per-skill <c>egress.allowHosts</c> is authoritative alone.
    /// </summary>
    public IReadOnlyList<string> AllowHosts { get; set; } = [];
}

/// <summary>
/// Configuration for built-in calculation and aggregation helpers,
/// nested under <c>Agents:Tools:BuiltIns:Calculation</c>.
/// </summary>
public sealed class BuiltInCalculationSettings
{
    /// <summary>
    /// Gets or sets the calculation/aggregation helper configuration.
    /// </summary>
    public CalculationSettings Calculation { get; set; } = new();
}

/// <summary>
/// Calculation/aggregation helper settings.
/// </summary>
public sealed class CalculationSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether calculation/aggregation helpers are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the upper bound for aggregate/group/count inputs.
    /// </summary>
    public int MaxInputItems { get; set; } = 1000;
}

/// <summary>
/// Database query tool configuration nested under <c>Agents:Tools:DatabaseQuery</c>.
/// </summary>
public sealed class DatabaseQuerySettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the database query tool is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of rows to return in a query.
    /// </summary>
    public int MaxRows { get; set; } = 200;

    /// <summary>
    /// Gets or sets the default timeout in seconds for a database query.
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the list of database query connections.
    /// </summary>
    public List<DatabaseQueryConnectionSettings> Connections { get; set; } = [];
}

/// <summary>
/// Configuration for a single database query connection.
/// </summary>
public sealed class DatabaseQueryConnectionSettings
{
    /// <summary>
    /// Gets or sets the name of the connection.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the database provider name: "postgres" or "sqlite".
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// Gets or sets the connection string.
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets whether the connection is read-only.
    /// </summary>
    public bool ReadOnly { get; set; } = true;

    /// <summary>
    /// Gets or sets the list of allowed schemas for this connection.
    /// </summary>
    public List<string> AllowedSchemas { get; set; } = [];
}

/// <summary>
/// Filesystem tool configuration nested under <c>Agents:Tools:FileSystem</c>.
/// </summary>
public sealed class FileSystemToolSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether filesystem tools are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

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

/// <summary>
/// Document ingestion tool configuration nested under <c>Agents:Tools:DocumentIngestion</c>.
/// </summary>
public sealed class DocumentIngestionToolSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether document ingestion is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent ingestion jobs.
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 3;

    /// <summary>
    /// Gets or sets the ingestion queue capacity.
    /// </summary>
    public int QueueCapacity { get; set; } = 100;

    /// <summary>
    /// Gets or sets the enqueue timeout in seconds.
    /// </summary>
    public int EnqueueTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the folder watch settle delay in seconds.
    /// </summary>
    public int WatchSettleDelaySeconds { get; set; } = 2;

    /// <summary>
    /// Gets or sets the maximum retry count for folder watch operations.
    /// </summary>
    public int WatchMaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay in seconds for retry backoff.
    /// </summary>
    public int WatchRetryBaseDelaySeconds { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum delay in seconds for retry backoff.
    /// </summary>
    public int WatchRetryMaxDelaySeconds { get; set; } = 60;
}