namespace LeanKernel.Logic.Tools;

/// <summary>
/// Manages registered LeanKernel tool definitions at runtime.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Gets all registered tool definitions.
    /// </summary>
    IReadOnlyList<ToolDefinition> Tools { get; }

    /// <summary>
    /// Registers a tool definition. Throws if a tool with the same name already exists.
    /// </summary>
    /// <param name="tool">The tool to register.</param>
    void Register(ToolDefinition tool);

    /// <summary>
    /// Attempts to register a tool definition. Returns false if the name is already taken.
    /// </summary>
    /// <param name="tool">The tool to register.</param>
    /// <returns>True when registered; false when the name was already in use.</returns>
    bool TryRegister(ToolDefinition tool);

    /// <summary>
    /// Returns a value indicating whether a tool with the given name is registered.
    /// </summary>
    /// <param name="name">The tool name to check.</param>
    /// <returns>True when a tool with that name is registered.</returns>
    bool Contains(string name);
}
