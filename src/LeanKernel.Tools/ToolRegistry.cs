using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Tools;

/// <summary>
/// Central tool registry. Discovers, stores, and filters tools by governance policy.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ToolGovernancePolicy _policy;
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(
        ToolGovernancePolicy policy,
        IEnumerable<ToolDefinition> tools,
        ILogger<ToolRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(logger);

        _policy = policy;
        _logger = logger;

        foreach (var tool in tools)
        {
            _tools[tool.Name] = tool;
        }

        _logger.LogInformation("Tool registry initialized with {Count} tools", _tools.Count);
    }

    public IReadOnlyList<ToolDefinition> GetVisibleTools(ToolVisibilityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var visible = _tools.Values
            .Where(tool => _policy.IsVisible(tool, context))
            .ToList();

        _logger.LogDebug(
            "Tool visibility: {Visible}/{Total} tools visible for context",
            visible.Count,
            _tools.Count);

        return visible;
    }

    public ToolDefinition? GetTool(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _tools.TryGetValue(name, out var tool);
        return tool;
    }
}
