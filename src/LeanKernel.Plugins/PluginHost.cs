using LeanKernel.Core.Interfaces;

namespace LeanKernel.Plugins;

/// <summary>
/// Runtime tool registry. In Phase 4, this will be replaced by
/// source-generated static registration. For now, uses DI enumeration.
/// </summary>
public sealed class PluginHost : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools;

    public PluginHost(IEnumerable<ITool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, t => t);
    }

    public IReadOnlyDictionary<string, ITool> Tools => _tools;

    public ITool? GetTool(string name) =>
        _tools.GetValueOrDefault(name);

    public IEnumerable<string> GetToolNames() =>
        _tools.Keys;
}
