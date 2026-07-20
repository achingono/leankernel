namespace LeanKernel.Logic.Tools;

/// <summary>
/// Defines the contract for a LeanKernel-owned built-in or dynamically-loaded tool.
/// </summary>
public sealed class ToolDefinition
{
    /// <summary>
    /// Gets or sets the stable tool name exposed to the model.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable tool description surfaced to the model.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the category used for allowlist governance.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the parameter schemas for this tool.
    /// </summary>
    public IReadOnlyList<ToolParameter> Parameters { get; set; } = [];

    /// <summary>
    /// Gets or sets the handler that executes this tool.
    /// </summary>
    public Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<ToolResult>> Handler { get; set; } = null!;
}