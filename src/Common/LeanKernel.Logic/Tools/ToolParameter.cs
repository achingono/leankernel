namespace LeanKernel.Logic.Tools;

/// <summary>
/// Describes a single input parameter for a tool definition.
/// </summary>
public sealed class ToolParameter
{
    /// <summary>
    /// Gets or sets the parameter name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON Schema type: string, integer, number, boolean.
    /// </summary>
    public string Type { get; set; } = "string";

    /// <summary>
    /// Gets or sets the parameter description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this parameter is required.
    /// </summary>
    public bool Required { get; set; }
}