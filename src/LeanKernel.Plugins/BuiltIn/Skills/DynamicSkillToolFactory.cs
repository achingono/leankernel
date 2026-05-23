using LeanKernel.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Plugins.BuiltIn.Skills;

/// <summary>
/// Factory that creates ITool instances from loaded skill definitions.
/// Enables runtime skill loading without recompilation.
/// </summary>
public sealed class DynamicSkillToolFactory
{
    private readonly ISkillRegistry _skillRegistry;
    private readonly IBinaryResolver _binaryResolver;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Represents the dynamic skill tool factory.
    /// </summary>
    public DynamicSkillToolFactory(
        ISkillRegistry skillRegistry,
        IBinaryResolver binaryResolver,
        ILoggerFactory loggerFactory)
    {
        _skillRegistry = skillRegistry;
        _binaryResolver = binaryResolver;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Create a tool from a skill definition.
    /// </summary>
    public async Task<ITool?> CreateToolAsync(string skillName)
    {
        var skill = await _skillRegistry.GetSkillAsync(skillName);
        if (skill == null)
            return null;

        var httpClient = CreateHttpClientForSkill(skill);
        var logger = _loggerFactory.CreateLogger<DynamicSkillTool>();
        return new DynamicSkillTool(skill, httpClient, _binaryResolver, logger);
    }

    /// <summary>
    /// Create all available tools from discovered skills.
    /// Only creates tools for skills that are available (not quarantined or missing binaries).
    /// </summary>
    public async Task<IReadOnlyList<ITool>> CreateAllToolsAsync()
    {
        var skills = await _skillRegistry.GetAllSkillsAsync();
        var tools = new List<ITool>();

        foreach (var skillDef in skills.Values)
        {
            if (skillDef.IsAvailable)
            {
                var httpClient = CreateHttpClientForSkill(skillDef);
                var logger = _loggerFactory.CreateLogger<DynamicSkillTool>();
                tools.Add(new DynamicSkillTool(skillDef, httpClient, _binaryResolver, logger));
            }
        }

        return tools;
    }

    /// <summary>
    /// Create an HttpClient with egress policy for a skill.
    /// </summary>
    private static HttpClient CreateHttpClientForSkill(SkillDefinition skill)
    {
        var allowHosts = skill.Runtime?.Egress.AllowHosts ?? [];
        var policy = new SkillEgressPolicy(allowHosts);
        var handler = new EgressPolicyHandler(policy);
        var httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(skill.Runtime?.TimeoutSeconds ?? 30)
        };
        return httpClient;
    }
}

/// <summary>
/// Dynamically populated tool registry that loads skills from the filesystem.
/// Supports hot reload when SKILL.md files change.
/// </summary>
public sealed class DynamicPluginHost : IToolRegistry
{
    private readonly DynamicSkillToolFactory _factory;
    private readonly Func<IReadOnlyList<ITool>> _builtInToolsFactory;
    private readonly Dictionary<string, ITool> _tools;
    private readonly ILogger<DynamicPluginHost> _logger;

    /// <summary>
    /// Gets or sets the tools.
    /// </summary>
    public IReadOnlyDictionary<string, ITool> Tools => _tools;

    /// <summary>
    /// Represents the dynamic plugin host.
    /// </summary>
    public DynamicPluginHost(
        DynamicSkillToolFactory factory,
        IEnumerable<ITool> builtInTools,
        ILogger<DynamicPluginHost> logger)
        : this(factory, () => builtInTools.ToList(), logger)
    {
    }

    /// <summary>
    /// Represents the dynamic plugin host.
    /// </summary>
    public DynamicPluginHost(
        DynamicSkillToolFactory factory,
        Func<IReadOnlyList<ITool>> builtInToolsFactory,
        ILogger<DynamicPluginHost> logger)
    {
        _factory = factory;
        _builtInToolsFactory = builtInToolsFactory;
        _logger = logger;
        _tools = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Initialize the registry by loading all discovered skills.
    /// Should be called during application startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            var builtInTools = _builtInToolsFactory();
            foreach (var tool in builtInTools)
            {
                _tools[tool.Name] = tool;
                _logger.LogInformation("Registered built-in tool: {ToolName}", tool.Name);
            }

            var tools = await _factory.CreateAllToolsAsync();
            foreach (var tool in tools)
            {
                _tools[tool.Name] = tool;
                _logger.LogInformation("Registered dynamic tool: {ToolName}", tool.Name);
            }
            _logger.LogInformation("Dynamic plugin host initialized with {Count} tools", _tools.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize dynamic skill tools");
        }
    }

    /// <summary>
    /// Refresh the registry with updated skills from disk.
    /// Called when SKILL.md files change (hot reload).
    /// </summary>
    public async Task RefreshAsync()
    {
        try
        {
            _logger.LogInformation("Refreshing dynamic plugin host");
            _tools.Clear();
            await InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh dynamic plugin host");
            throw;
        }
    }

    /// <summary>
    /// Executes the get tool operation.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The operation result.</returns>
    public ITool? GetTool(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;

    /// <summary>
    /// Executes the get tool names operation.
    /// </summary>
    /// <returns>The operation result.</returns>
    public IEnumerable<string> GetToolNames() =>
        _tools.Keys;
}
