namespace LeanKernel.Logic.Providers;

/// <summary>
/// A no-op / stub memory client for environments where Memory is not available.
/// </summary>
public sealed class StubMemoryClient : IMemoryClient
{
    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(
        MemoryScope scope,
        string query,
        int maxResults = 10,
        CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<MemoryItem>>(Array.Empty<MemoryItem>());
    }

    /// <inheritdoc />
    public Task SaveMemoryAsync(
        MemoryScope scope,
        string key,
        string content,
        CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}