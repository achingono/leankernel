namespace LeanKernel.Logic.Providers;

/// <summary>
/// Represents a single memory item retrieved from the memory store.
/// </summary>
public sealed class MemoryItem
{
    /// <summary>
    /// Gets the unique key for this memory.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets the textual content of the memory.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Gets the relevance score.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Gets the source or origin of the memory.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Gets the channel identifier parsed from the scoped memory key when available.
    /// </summary>
    public Guid? ChannelId { get; init; }

    /// <summary>
    /// Gets the scope-relative key when available.
    /// </summary>
    public string? ScopeRelativeKey { get; init; }
}