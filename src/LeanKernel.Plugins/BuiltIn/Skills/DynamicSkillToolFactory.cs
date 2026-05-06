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
    private readonly HttpClient _httpClient;
    private readonly ILoggerFactory _loggerFactory;

    public DynamicSkillToolFactory(
        ISkillRegistry skillRegistry,
        HttpClient httpClient,
        ILoggerFactory loggerFactory)
    {
        _skillRegistry = skillRegistry;
        _httpClient = httpClient;
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

        var logger = _loggerFactory.CreateLogger<DynamicSkillTool>();
        return new DynamicSkillTool(skill, _httpClient, logger);
    }

    /// <summary>
    /// Create all available tools from discovered skills.
    /// </summary>
    public async Task<IReadOnlyList<ITool>> CreateAllToolsAsync()
    {
        var skills = await _skillRegistry.GetAllSkillsAsync();
        var tools = new List<ITool>();

        foreach (var skillDef in skills.Values)
        {
            var logger = _loggerFactory.CreateLogger<DynamicSkillTool>();
            tools.Add(new DynamicSkillTool(skillDef, _httpClient, logger));
        }

        return tools;
    }
}

/// <summary>
/// Dynamically populated tool registry that loads skills from the filesystem.
/// </summary>
public sealed class DynamicPluginHost : IToolRegistry
{
    private readonly ISkillRegistry _skillRegistry;
    private readonly DynamicSkillToolFactory _factory;
    private readonly Dictionary<string, ITool> _tools;
    private readonly ILogger<DynamicPluginHost> _logger;

    public IReadOnlyDictionary<string, ITool> Tools => _tools;

    public DynamicPluginHost(
        ISkillRegistry skillRegistry,
        DynamicSkillToolFactory factory,
        ILogger<DynamicPluginHost> logger)
    {
        _skillRegistry = skillRegistry;
        _factory = factory;
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
            var tools = await _factory.CreateAllToolsAsync();
            foreach (var tool in tools)
            {
                _tools[tool.Name] = tool;
                _logger.LogInformation("Registered dynamic tool: {ToolName}", tool.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize dynamic skill tools");
        }
    }

    public ITool? GetTool(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;

    public IEnumerable<string> GetToolNames() =>
        _tools.Keys;
}
