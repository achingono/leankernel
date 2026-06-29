using System.Collections.Concurrent;

namespace LeanKernel.Learning;

/// <summary>
/// Coordinates concurrent updates to knowledge pages by acquiring per-page locks.
/// Prevents race conditions when multiple learning steps or workers attempt to
/// read-modify-write the same knowledge page simultaneously.
/// </summary>
public sealed class KnowledgePageUpdateCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Executes an action against a specific knowledge page while holding an exclusive lock for that page.
    /// Multiple pages can be updated concurrently, but updates to the same page are serialized.
    /// </summary>
    /// <typeparam name="T">The return type of the action.</typeparam>
    /// <param name="pageKey">The knowledge page key to lock on.</param>
    /// <param name="action">The action to execute while holding the lock.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The result of the action.</returns>
    public async Task<T> ExecuteAsync<T>(string pageKey, Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pageKey);
        ArgumentNullException.ThrowIfNull(action);

        var gate = _locks.GetOrAdd(pageKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            return await action(ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
