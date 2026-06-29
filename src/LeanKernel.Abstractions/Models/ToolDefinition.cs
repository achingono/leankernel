namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Defines the structure and behavior of a tool.
/// </summary>
public sealed record ToolDefinition
{
    /// <summary>
    /// Gets the unique name of the tool.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the description of the tool's functionality.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the optional category of the tool.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Gets the list of parameters for the tool.
    /// </summary>
    public IReadOnlyList<ToolParameter>? Parameters { get; init; }

    /// <summary>
    /// Gets the function that handles the tool execution.
    /// </summary>
    public Func<IDictionary<string, object?>, CancellationToken, Task<ToolResult>>? Handler { get; init; }
}

/// <summary>
/// Defines a parameter for a tool.
/// </summary>
public sealed record ToolParameter
{
    /// <summary>
    /// Gets the name of the parameter.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the type of the parameter.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the description of the parameter.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets a value indicating whether the parameter is required.
    /// </summary>
    public bool Required { get; init; } = true;
}
