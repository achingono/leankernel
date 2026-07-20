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