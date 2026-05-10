using System.Diagnostics.CodeAnalysis;

namespace LeanKernel.Plugins.Sdk;

/// <summary>
/// Annotate ITool implementations with this attribute for compile-time
/// discovery by the ToolRegistryGenerator source generator.
/// </summary>
[ExcludeFromCodeCoverage]
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ToolMetadataAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public required string Description { get; init; }
    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public ToolCategory Category { get; init; } = ToolCategory.General;
}

/// <summary>
/// Represents the available tool category values.
/// </summary>
public enum ToolCategory
{
    /// <summary>
    /// General-purpose tools.
    /// </summary>
    General,

    /// <summary>
    /// Information retrieval tools.
    /// </summary>
    Information,

    /// <summary>
    /// File-system tools.
    /// </summary>
    FileSystem,

    /// <summary>
    /// Communication tools.
    /// </summary>
    Communication,

    /// <summary>
    /// Scheduling tools.
    /// </summary>
    Scheduling,

    /// <summary>
    /// Code-related tools.
    /// </summary>
    Code,

    /// <summary>
    /// Wiki and memory tools.
    /// </summary>
    Wiki
}
