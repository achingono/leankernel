namespace LeanKernel.Logic.Tools;

/// <summary>
/// Represents the outcome of a tool invocation.
/// </summary>
public sealed class ToolResult
{
    /// <summary>
    /// Gets or sets the name of the tool that produced this result.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the invocation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the serialized output produced by the tool when successful.
    /// </summary>
    public string? Output { get; set; }

    /// <summary>
    /// Gets or sets the error message when the invocation failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Returns the output if successful, or the error message, for use as the function-call result.
    /// </summary>
    /// <returns>The effective result text.</returns>
    public override string ToString() =>
        Success ? (Output ?? string.Empty) : $"Error: {Error ?? "Unknown error"}";
}