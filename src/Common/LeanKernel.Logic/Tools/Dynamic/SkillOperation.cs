namespace LeanKernel.Logic.Tools.Dynamic;

/// <summary>
/// A single HTTP operation derived from a SKILL.md definition.
/// </summary>
public sealed class SkillOperation
{
    /// <summary>
    /// Gets the operation identifier, unique within the skill. Becomes the tool-name suffix.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the operation summary surfaced to the model.
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Gets the HTTP method for this operation.
    /// </summary>
    public string HttpMethod { get; init; } = "GET";

    /// <summary>
    /// Gets the HTTP path template. {placeholder} segments are substituted from arguments.
    /// </summary>
    public string HttpPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the declared parameters for this operation.
    /// </summary>
    public IReadOnlyList<SkillOperationParameter> Parameters { get; init; } = [];
}