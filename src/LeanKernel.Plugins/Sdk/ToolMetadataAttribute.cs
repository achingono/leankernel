namespace LeanKernel.Plugins.Sdk;

/// <summary>
/// Annotate ITool implementations with this attribute for compile-time
/// discovery by the ToolRegistryGenerator source generator.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ToolMetadataAttribute : Attribute
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public ToolCategory Category { get; init; } = ToolCategory.General;
}

public enum ToolCategory
{
    General,
    Information,
    FileSystem,
    Communication,
    Scheduling,
    Code,
    Wiki
}
