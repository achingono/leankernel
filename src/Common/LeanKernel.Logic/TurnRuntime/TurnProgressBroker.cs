namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// In-memory pub/sub broker for turn progress events.
/// Subscribers receive <see cref="TurnProgressUpdate"/> messages for long-running turns.
/// </summary>
public sealed class TurnProgressBroker
{
    private readonly Dictionary<string, Dictionary<Guid, Func<TurnProgressUpdate, Task>>> _subscribers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    /// <summary>
    /// Subscribes to progress updates for a specific conversation.
    /// Returns a disposable that unsubscribes when disposed.
    /// </summary>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <param name="handler">The handler to invoke on progress updates.</param>
    public IDisposable Subscribe(string conversationId, Func<TurnProgressUpdate, Task> handler)
    {
        var subscriptionId = Guid.NewGuid();

        lock (_lock)
        {
            if (!_subscribers.TryGetValue(conversationId, out var handlers))
            {
                handlers = new Dictionary<Guid, Func<TurnProgressUpdate, Task>>();
                _subscribers[conversationId] = handlers;
            }

            handlers[subscriptionId] = handler;
        }

        return new Subscription(this, conversationId, subscriptionId);
    }

    /// <summary>
    /// Publishes a progress update to all subscribers of the given conversation.
    /// Exceptions in handlers are isolated and do not affect other subscribers.
    /// </summary>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <param name="update">The progress update to publish.</param>
    public async Task PublishAsync(string conversationId, TurnProgressUpdate update)
    {
        Func<TurnProgressUpdate, Task>[] handlers;
        lock (_lock)
        {
            if (!_subscribers.TryGetValue(conversationId, out var subs) || subs.Count == 0)
            {
                return;
            }

            handlers = [.. subs.Values];
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler(update).ConfigureAwait(false);
            }
            catch
            {
                // Isolate handler failures from other subscribers.
            }
        }
    }

    private void Unsubscribe(string conversationId, Guid subscriptionId)
    {
        lock (_lock)
        {
            if (_subscribers.TryGetValue(conversationId, out var subs))
            {
                subs.Remove(subscriptionId);
                if (subs.Count == 0)
                {
                    _subscribers.Remove(conversationId);
                }
            }
        }
    }

    private sealed class Subscription(TurnProgressBroker broker, string conversationId, Guid subscriptionId) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                broker.Unsubscribe(conversationId, subscriptionId);
                _disposed = true;
            }
        }
    }
}

/// <summary>
/// A progress update for a long-running turn.
/// </summary>
public sealed class TurnProgressUpdate
{
    /// <summary>
    /// The conversation this update belongs to.
    /// </summary>
    public required string ConversationId { get; init; }

    /// <summary>
    /// Human-readable status message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Current stage name.
    /// </summary>
    public string? Stage { get; init; }

    /// <summary>
    /// Progress percentage (0-100), if known.
    /// </summary>
    public int? PercentComplete { get; init; }

    /// <summary>
    /// UTC timestamp of the update.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}