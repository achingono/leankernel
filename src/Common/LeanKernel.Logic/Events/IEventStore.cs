namespace LeanKernel.Logic.Events;

/// <summary>
/// Async persistence contract for the event spine.
/// Implementations may write to a dedicated event table, a message bus,
/// or a logging sink depending on deployment environment.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Appends a single event record to the store.
    /// </summary>
    /// <param name="eventRecord">The event record to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AppendAsync(object eventRecord, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a batch of event records to the store in a single operation.
    /// </summary>
    /// <param name="eventRecords">The event records to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AppendBatchAsync(IEnumerable<object> eventRecords, CancellationToken cancellationToken = default);
}