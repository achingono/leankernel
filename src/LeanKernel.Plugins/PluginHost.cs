using LeanKernel.Core.Interfaces;

namespace LeanKernel.Plugins;

/// <summary>
/// Runtime tool registry built from the tools registered in dependency injection.
/// </summary>
public sealed class PluginHost : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginHost" /> class.
    /// </summary>
    /// <param name="tools">The tools to expose through the registry.</param>
    public PluginHost(IEnumerable<ITool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, t => t);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ITool> Tools => _tools;

    /// <inheritdoc />
    public ITool? GetTool(string name) =>
        _tools.GetValueOrDefault(name);

    /// <inheritdoc />
    public IEnumerable<string> GetToolNames() =>
        _tools.Keys;
}
