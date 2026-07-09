using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Learning;

/// <summary>
/// Bounded channel-based queue for turn events that implements <see cref="ITurnEventSink"/>.
/// Drops the oldest events when the queue reaches capacity to prevent unbounded memory growth.
/// Used to decouple event publication from background learning processing.
/// </summary>
public sealed class TurnEventQueue(
    IOptions<LearningConfig> config,
    ILogger<TurnEventQueue> logger) : ITurnEventSink
{
    private readonly object _sync = new();
    private readonly ILogger<TurnEventQueue> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly int _capacity = Math.Max(1, (config ?? throw new ArgumentNullException(nameof(config))).Value.QueueCapacity);
    private readonly Channel<TurnEvent> _channel = Channel.CreateBounded<TurnEvent>(new BoundedChannelOptions(Math.Max(1, config.Value.QueueCapacity))
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = false,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private int _bufferedCount;
    private bool _completed;

    /// <summary>
    /// Gets the current number of buffered items in the queue.
    /// </summary>
    public int BufferedCount
    {
        get
        {
            lock (_sync)
            {
                return _bufferedCount;
            }
        }
    }

    /// <summary>
    /// Publishes a turn event to the queue for background processing.
    /// If the queue is full, the oldest event is dropped and a warning is logged.
    /// </summary>
    /// <param name="turnEvent">The turn event to enqueue.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that completes when the event has been enqueued or dropped.</returns>
    public Task PublishAsync(TurnEvent turnEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(turnEvent);
        ct.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_completed)
            {
                _logger.LogDebug(
                    "Skipping turn event for session {SessionId} turn {TurnId} because the learning queue is completed",
                    turnEvent.SessionId,
                    turnEvent.TurnId);
                return Task.CompletedTask;
            }

            var queueWasFull = _bufferedCount >= _capacity;
            if (!_channel.Writer.TryWrite(turnEvent))
            {
                _logger.LogWarning(
                    "Failed to enqueue turn event for session {SessionId} turn {TurnId}; the learning queue rejected the item",
                    turnEvent.SessionId,
                    turnEvent.TurnId);
                return Task.CompletedTask;
            }

            _bufferedCount = Math.Min(_capacity, _bufferedCount + 1);

            if (queueWasFull)
            {
                _logger.LogWarning(
                    "Learning queue reached capacity {Capacity}; dropping the oldest queued turn event before enqueuing session {SessionId} turn {TurnId}",
                    _capacity,
                    turnEvent.SessionId,
                    turnEvent.TurnId);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Asynchronously waits for data to be available to read from the queue.
    /// </summary>
    /// <param name="ct">A cancellation token that can be used to cancel the wait.</param>
    /// <returns>A value task that completes when data is available or the channel is closed.</returns>
    public ValueTask<bool> WaitToReadAsync(CancellationToken ct = default)
        => _channel.Reader.WaitToReadAsync(ct);

    /// <summary>
    /// Attempts to read a turn event from the queue without blocking.
    /// </summary>
    /// <param name="turnEvent">The turn event if successfully read; otherwise, <c>null</c>.</param>
    /// <returns><c>true</c> if a turn event was read; otherwise, <c>false</c>.</returns>
    public bool TryRead([NotNullWhen(true)] out TurnEvent? turnEvent)
    {
        lock (_sync)
        {
            var success = _channel.Reader.TryRead(out turnEvent);
            if (success && _bufferedCount > 0)
            {
                _bufferedCount--;
            }

            return success;
        }
    }

    /// <summary>
    /// Marks the queue as complete, signaling that no more events will be published.
    /// </summary>
    public void Complete()
    {
        lock (_sync)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _channel.Writer.TryComplete();
        }
    }
}
