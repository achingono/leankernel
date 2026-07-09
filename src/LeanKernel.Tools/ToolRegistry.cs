using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Tools;

/// <summary>
/// Central tool registry. Discovers, stores, and filters tools by governance policy.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly object _sync = new();
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

        List<ToolDefinition> snapshot;
        lock (_sync)
        {
            snapshot = [.. _tools.Values];
        }

        var visible = snapshot
            .Where(tool => _policy.IsVisible(tool, context))
            .ToList();

        _logger.LogDebug(
            "Tool visibility: {Visible}/{Total} tools visible for context",
            visible.Count,
            snapshot.Count);

        return visible;
    }

    public ToolDefinition? GetTool(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ToolDefinition? tool;
        lock (_sync)
        {
            _tools.TryGetValue(name, out tool);
        }

        return tool;
    }

    public void AddTools(IEnumerable<ToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var count = 0;
        foreach (var tool in tools)
        {
            lock (_sync)
            {
                if (_tools.TryAdd(tool.Name, tool))
                {
                    count++;
                }
            }
        }

        if (count > 0)
        {
            var total = 0;
            lock (_sync)
            {
                total = _tools.Count;
            }

            _logger.LogInformation("Tool registry added {Count} new tools (total: {Total})", count, total);
        }
    }
}
