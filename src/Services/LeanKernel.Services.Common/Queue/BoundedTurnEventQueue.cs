using System.Threading.Channels;

using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Common.Queue;

/// <summary>
/// Bounded channel-backed implementation of <see cref="ITurnEventQueue"/>.
/// </summary>
/// <param name="capacity">Maximum number of retained events.</param>
public sealed class BoundedTurnEventQueue(int capacity) : ITurnEventQueue
{
    private readonly Channel<CompletedTurnEvent> _channel = Channel.CreateBounded<CompletedTurnEvent>(
        new BoundedChannelOptions(Math.Max(1, capacity))
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    /// <inheritdoc />
    public ValueTask<bool> EnqueueAsync(CompletedTurnEvent completedTurn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completedTurn);
        var accepted = _channel.Writer.TryWrite(completedTurn);
        return ValueTask.FromResult(accepted);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<CompletedTurnEvent> DequeueAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
