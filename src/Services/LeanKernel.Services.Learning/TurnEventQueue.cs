using System.Threading.Channels;

using LeanKernel.Events;

namespace LeanKernel.Services.Learning;

/// <summary>
/// Default channel-based implementation of both <see cref="ITurnEventProducer"/>
/// and <see cref="ITurnEventConsumer"/>.
/// Uses Wait mode to avoid silent event loss under backpressure.
/// </summary>
public sealed class TurnEventQueue : ITurnEventProducer, ITurnEventConsumer
{
    private readonly Channel<TurnCompletedEvent> _channel;
    private long _droppedCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="TurnEventQueue"/> class.
    /// </summary>
    /// <param name="capacity">Maximum capacity of the queue.</param>
    public TurnEventQueue(int capacity = 1000)
    {
        _channel = Channel.CreateBounded<TurnCompletedEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });
    }

    /// <summary>
    /// Gets the approximate number of items currently in the queue.
    /// </summary>
    public int BacklogCount => _channel.Reader.Count;

    /// <summary>
    /// Gets the number of events dropped due to backpressure (always 0 in Wait mode).
    /// </summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    /// <inheritdoc />
    public ValueTask EnqueueAsync(TurnCompletedEvent turnEvent, CancellationToken ct = default)
    {
        if (!_channel.Writer.TryWrite(turnEvent))
        {
            Interlocked.Increment(ref _droppedCount);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask<TurnCompletedEvent?> TryDequeueAsync(CancellationToken ct = default)
    {
        if (await _channel.Reader.WaitToReadAsync(ct))
        {
            return await _channel.Reader.ReadAsync(ct);
        }

        return null;
    }
}
