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
