using Microsoft.Extensions.Logging;

namespace LeanKernel.Gateway.Channels;

/// <summary>
/// Maintains a channel typing indicator by periodically refreshing it until stopped.
/// </summary>
public sealed class TypingIndicatorKeepAlive : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly string _recipientId;
    private readonly ILogger _logger;
    private readonly PeriodicTimer? _timer;
    private readonly CancellationTokenSource _loopCts;
    private readonly Task? _loopTask;
    private readonly TimeSpan _stopTimeout;
    private Task? _initialStartTask;
    private int _stopped;

    private TypingIndicatorKeepAlive(
        IChannel channel,
        string recipientId,
        TypingSettings config,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken ct)
    {
        _channel = channel;
        _recipientId = recipientId;
        _logger = logger;
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _stopTimeout = TimeSpan.FromSeconds(Math.Max(1, config.StopTimeoutSeconds));

        if (config.Enabled)
        {
            var period = TimeSpan.FromSeconds(Math.Max(1, config.KeepAliveSeconds));
            _timer = new PeriodicTimer(period, timeProvider);
            _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token));
        }
    }

    /// <summary>
    /// Starts a new typing indicator keep-alive for the given channel and recipient.
    /// </summary>
    public static TypingIndicatorKeepAlive Start(
        IChannel channel,
        string recipientId,
        TypingSettings config,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientId);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        var keepAlive = new TypingIndicatorKeepAlive(channel, recipientId, config, timeProvider, logger, ct);
        keepAlive._initialStartTask = keepAlive.TryStartTypingAsync(ct);
        return keepAlive;
    }

    /// <summary>
    /// Stops the keep-alive loop and sends the stop-typing signal.
    /// </summary>
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        if (_initialStartTask is not null)
        {
            try
            {
                await _initialStartTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _loopCts.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        using var stopCts = new CancellationTokenSource(_stopTimeout);
        try
        {
            await _channel.StopTypingAsync(_recipientId, stopCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Typing keep-alive stop failed for {ChannelId} -> {RecipientId}",
                _channel.ChannelId,
                _recipientId);
        }

        _timer?.Dispose();
        _loopCts.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
        => new(StopAsync());

    private async Task RunLoopAsync(CancellationToken ct)
    {
        if (_timer is null)
        {
            return;
        }

        while (await _timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            await TryStartTypingAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task TryStartTypingAsync(CancellationToken ct)
    {
        try
        {
            await _channel.StartTypingAsync(_recipientId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Typing keep-alive refresh failed for {ChannelId} -> {RecipientId}",
                _channel.ChannelId,
                _recipientId);
        }
    }
}
