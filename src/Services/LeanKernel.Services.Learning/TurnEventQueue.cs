using System.Threading.Channels;

using LeanKernel.Events;

namespace LeanKernel.Services.Learning;

/// <summary>
/// Default channel-based implementation of both <see cref="ITurnEventProducer"/>
/// and <see cref="ITurnEventConsumer"/>.
/// </summary>
public sealed class TurnEventQueue : ITurnEventProducer, ITurnEventConsumer
{
    private readonly Channel<TurnCompletedEvent> _channel;

    /// <summary>
    /// Initializes a new instance of the <see cref="TurnEventQueue"/> class.
    /// </summary>
    /// <param name="capacity">Maximum capacity of the queue before old events are dropped.</param>
    public TurnEventQueue(int capacity = 1000)
    {
        _channel = Channel.CreateBounded<TurnCompletedEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    }

    /// <inheritdoc />
    public ValueTask EnqueueAsync(TurnCompletedEvent turnEvent, CancellationToken ct = default)
    {
        _ = _channel.Writer.TryWrite(turnEvent);
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