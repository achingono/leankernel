using Microsoft.Extensions.Options;

namespace LeanKernel.Channels.Signal;

public sealed class TypingIndicatorKeepAlive : IAsyncDisposable
{
    private readonly ITransportClient _transport;
    private readonly string _account;
    private readonly string _recipient;
    private readonly ILogger _logger;
    private readonly SignalSettings _settings;
    private readonly PeriodicTimer? _timer;
    private readonly CancellationTokenSource _loopCts;
    private readonly Task? _loopTask;
    private int _stopped;

    private TypingIndicatorKeepAlive(
        ITransportClient transport,
        string account,
        string recipient,
        SignalSettings settings,
        ILogger logger,
        CancellationToken ct)
    {
        _transport = transport;
        _account = account;
        _recipient = recipient;
        _logger = logger;
        _settings = settings;
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (settings.TypingIndicatorEnabled)
        {
            var keepAliveSeconds = Math.Max(1, settings.TypingKeepAliveSeconds);
            _timer = new PeriodicTimer(TimeSpan.FromSeconds(keepAliveSeconds));
            _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token));
        }
    }

    public static TypingIndicatorKeepAlive Start(
        ITransportClient transport,
        string account,
        string recipient,
        SignalSettings settings,
        ILogger logger,
        CancellationToken ct)
    {
        var keepAlive = new TypingIndicatorKeepAlive(transport, account, recipient, settings, logger, ct);
        _ = keepAlive.TryStartTypingAsync(ct);
        return keepAlive;
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        _loopCts.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_settings.TypingIndicatorEnabled)
        {
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _settings.TypingStopTimeoutSeconds)));
            try
            {
                await _transport.StopTypingAsync(_account, _recipient, stopCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Signal typing-indicator stop failed for {Recipient}.", _recipient);
            }
        }

        _timer?.Dispose();
        _loopCts.Dispose();
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task RunLoopAsync(CancellationToken ct)
    {
        if (_timer is null)
        {
            return;
        }

        while (await _timer.WaitForNextTickAsync(ct))
        {
            await TryStartTypingAsync(ct);
        }
    }

    private async Task TryStartTypingAsync(CancellationToken ct)
    {
        try
        {
            await _transport.StartTypingAsync(_account, _recipient, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Signal typing-indicator refresh failed for {Recipient}.", _recipient);
        }
    }
}
