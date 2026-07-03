using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Agents;

/// <summary>
/// In-memory broker for turn progress events.
/// </summary>
public sealed class TurnProgressBroker : ITurnProgressBroker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<Guid, Func<TurnProgressUpdate, Task>>> _subscribers = new(StringComparer.Ordinal);

    public IDisposable Subscribe(string sessionId, Func<TurnProgressUpdate, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(handler);

        var id = Guid.NewGuid();
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(sessionId, out var handlers))
            {
                handlers = new Dictionary<Guid, Func<TurnProgressUpdate, Task>>();
                _subscribers[sessionId] = handlers;
            }

            handlers[id] = handler;
        }

        return new Subscription(this, sessionId, id);
    }

    public async Task PublishAsync(TurnProgressUpdate update, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (ct.IsCancellationRequested)
        {
            return;
        }

        List<Func<TurnProgressUpdate, Task>> handlers;
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(update.SessionId, out var sessionSubscribers) || sessionSubscribers.Count == 0)
            {
                return;
            }

            handlers = sessionSubscribers.Values.ToList();
        }

        var publishTasks = handlers
            .Select(handler => InvokeSafelyAsync(handler, update))
            .ToArray();

        await Task.WhenAll(publishTasks).ConfigureAwait(false);
    }

    private static async Task InvokeSafelyAsync(Func<TurnProgressUpdate, Task> handler, TurnProgressUpdate update)
    {
        try
        {
            await handler(update).ConfigureAwait(false);
        }
        catch
        {
            // Publish is exception-isolated by contract.
        }
    }

    private void Unsubscribe(string sessionId, Guid id)
    {
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(sessionId, out var handlers))
            {
                return;
            }

            handlers.Remove(id);
            if (handlers.Count == 0)
            {
                _subscribers.Remove(sessionId);
            }
        }
    }

    private sealed class Subscription(TurnProgressBroker broker, string sessionId, Guid id) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            broker.Unsubscribe(sessionId, id);
        }
    }
}
