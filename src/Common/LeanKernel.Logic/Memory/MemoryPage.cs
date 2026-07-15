namespace LeanKernel.Logic.Memory;

/// <summary>
/// Represents a page retrieved from memory.
/// </summary>
public sealed class MemoryPage
{
    /// <summary>
    /// Gets the page key (slug).
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets the page content.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Gets the last modification date, if available.
    /// </summary>
    public DateTimeOffset? LastModified { get; init; }
}
