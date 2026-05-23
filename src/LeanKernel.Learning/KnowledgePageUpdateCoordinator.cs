using System.Collections.Concurrent;

namespace LeanKernel.Learning;

public sealed class KnowledgePageUpdateCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

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
