namespace LeanKernel.Logic.Providers;

/// <summary>
/// Represents the scope within which memories are stored and retrieved.
/// </summary>
public sealed class MemoryScope
{
    /// <summary>
    /// Gets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; init; }

    /// <summary>
    /// Gets the user identifier.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the channel identifier.
    /// </summary>
    public Guid ChannelId { get; init; }

    /// <summary>
    /// Gets the optional namespace for memory admission policy.
    /// </summary>
    public string? Namespace { get; init; }
}

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
}

/// <summary>
/// Provides memory search and persistence capabilities backed by GBrain or an in-memory stub.
/// </summary>
public interface IMemoryClient
{
    /// <summary>
    /// Searches for memories matching the query within the given scope.
    /// </summary>
    Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(
        MemoryScope scope,
        string query,
        int maxResults = 10,
        CancellationToken ct = default);

    /// <summary>
    /// Saves a memory item within the given scope.
    /// </summary>
    Task SaveMemoryAsync(
        MemoryScope scope,
        string key,
        string content,
        CancellationToken ct = default);
}

/// <summary>
/// A no-op / stub memory client for environments where GBrain is not available.
/// </summary>
public sealed class StubMemoryClient : IMemoryClient
{
    public Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(
        MemoryScope scope,
        string query,
        int maxResults = 10,
        CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<MemoryItem>>(Array.Empty<MemoryItem>());
    }

    public Task SaveMemoryAsync(
        MemoryScope scope,
        string key,
        string content,
        CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
