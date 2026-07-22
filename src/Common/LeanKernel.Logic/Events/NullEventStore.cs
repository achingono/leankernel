namespace LeanKernel.Logic.Events;

using Microsoft.Extensions.Logging;

/// <summary>
/// No-op event store that logs events at debug level without persisting.
/// Used as the default implementation until a concrete store is configured.
/// </summary>
public sealed class NullEventStore : IEventStore
{
    private readonly ILogger<NullEventStore>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NullEventStore"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for debug output.</param>
    public NullEventStore(ILogger<NullEventStore>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task AppendAsync(object eventRecord, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("EventStore (no-op): {EventType} emitted.", eventRecord.GetType().Name);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AppendBatchAsync(IEnumerable<object> eventRecords, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("EventStore (no-op): {Count} events emitted.", eventRecords.Count());
        return Task.CompletedTask;
    }
}