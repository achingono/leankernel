namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents a page in the knowledge base.
/// </summary>
public sealed record KnowledgePage
{
    /// <summary>
    /// Gets the unique key for the knowledge page.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the content of the knowledge page.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets the timestamp when the page was last modified.
    /// </summary>
    public DateTimeOffset? LastModified { get; init; }

    /// <summary>
    /// Gets the list of keys for linked pages.
    /// </summary>
    public IReadOnlyList<string>? LinkedPages { get; init; }
}
