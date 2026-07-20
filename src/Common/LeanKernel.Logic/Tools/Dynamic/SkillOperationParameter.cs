namespace LeanKernel.Logic.Tools.Dynamic;

/// <summary>
/// A parameter declared in a SKILL.md operation.
/// </summary>
public sealed class SkillOperationParameter
{
    /// <summary>
    /// Gets the parameter name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the parameter type: string, integer, number, boolean.
    /// </summary>
    public string Type { get; init; } = "string";

    /// <summary>
    /// Gets the parameter description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the parameter is required.
    /// </summary>
    public bool Required { get; init; }
}