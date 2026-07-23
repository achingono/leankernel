namespace LeanKernel.Logic.Events;

/// <summary>
/// Event subscriber that persists collected events to <see cref="IEventStore"/>.
/// Replaces the direct <c>eventStore.AppendBatchAsync</c> call in the flush path.
/// </summary>
public sealed class PersistEventSubscriber : IEventSubscriber
{
    private readonly IEventStore _eventStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="PersistEventSubscriber"/> class.
    /// </summary>
    /// <param name="eventStore">The event store for persistence.</param>
    public PersistEventSubscriber(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    /// <inheritdoc />
    public async Task HandleAsync(IReadOnlyList<object> events, CancellationToken ct = default)
    {
        if (events.Count == 0)
        {
            return;
        }

        await _eventStore.AppendBatchAsync(events, ct);
    }
}
