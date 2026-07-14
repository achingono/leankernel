using System.Collections.Concurrent;

namespace LeanKernel.Logic.Tools;

/// <summary>
/// Thread-safe, in-process registry of LeanKernel tool definitions.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, ToolDefinition> _tools =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyList<ToolDefinition> Tools => [.. _tools.Values];

    /// <inheritdoc />
    public void Register(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (!TryRegister(tool))
        {
            throw new InvalidOperationException(
                $"A tool named '{tool.Name}' is already registered.");
        }
    }

    /// <inheritdoc />
    public bool TryRegister(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentException.ThrowIfNullOrWhiteSpace(tool.Name);

        return _tools.TryAdd(tool.Name, tool);
    }

    /// <inheritdoc />
    public bool Contains(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _tools.ContainsKey(name);
    }
}
