using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Learning;

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

    public Task PublishAsync(TurnEvent turnEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(turnEvent);

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

            if (queueWasFull)
            {
                _logger.LogWarning(
                    "Learning queue reached capacity {Capacity}; dropping the oldest queued turn event before enqueuing session {SessionId} turn {TurnId}",
                    _capacity,
                    turnEvent.SessionId,
                    turnEvent.TurnId);
            }
            else
            {
                _bufferedCount++;
            }
        }

        return Task.CompletedTask;
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken ct = default)
        => _channel.Reader.WaitToReadAsync(ct);

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
