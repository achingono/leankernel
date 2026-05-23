using System.Collections.Concurrent;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Learning;

public sealed class LearningBackgroundWorker(
    TurnEventQueue queue,
    ISelfImprovementPipeline pipeline,
    IOptions<LearningConfig> config,
    ILogger<LearningBackgroundWorker> logger) : IHostedService, IDisposable
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

    private readonly TurnEventQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    private readonly ISelfImprovementPipeline _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    private readonly LearningConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
    private readonly ILogger<LearningBackgroundWorker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly ConcurrentDictionary<long, Task> _inFlight = [];
    private readonly SemaphoreSlim _concurrencyGate = new(Math.Max(1, config.Value.MaxConcurrentLearningTasks));
    private Task? _runTask;
    private long _workItemId;

    public Task StartAsync(CancellationToken ct)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("Learning background worker is disabled via configuration");
            return Task.CompletedTask;
        }

        _runTask ??= RunAsync(_shutdownCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_runTask is null)
        {
            return;
        }

        _queue.Complete();

        var completedTask = await Task.WhenAny(_runTask, Task.Delay(DrainTimeout, ct)).ConfigureAwait(false);
        if (completedTask != _runTask)
        {
            _logger.LogWarning(
                "Learning worker shutdown exceeded {TimeoutSeconds}s; abandoning {BufferedCount} queued events with {InFlightCount} work items still running",
                DrainTimeout.TotalSeconds,
                _queue.BufferedCount,
                _inFlight.Count);
            _shutdownCts.Cancel();
        }

        try
        {
            await _runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        _shutdownCts.Dispose();
        _concurrencyGate.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (await _queue.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_queue.TryRead(out var turnEvent))
                {
                    await _concurrencyGate.WaitAsync(ct).ConfigureAwait(false);

                    var workItemId = Interlocked.Increment(ref _workItemId);
                    var task = ProcessTurnEventAsync(workItemId, turnEvent, ct);
                    _inFlight[workItemId] = task;
                    _ = ObserveCompletionAsync(workItemId, task);
                }
            }

            await AwaitInFlightAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Learning worker loop cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Learning worker terminated unexpectedly");
        }
    }

    private async Task ProcessTurnEventAsync(long workItemId, TurnEvent turnEvent, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug(
                "Processing queued learning event {WorkItemId} for session {SessionId} turn {TurnId}",
                workItemId,
                turnEvent.SessionId,
                turnEvent.TurnId);
            await _pipeline.ProcessTurnEventAsync(turnEvent, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Learning event processing failed for session {SessionId} turn {TurnId}",
                turnEvent.SessionId,
                turnEvent.TurnId);
        }
    }

    private async Task ObserveCompletionAsync(long workItemId, Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
        }
        finally
        {
            _inFlight.TryRemove(workItemId, out _);
            _concurrencyGate.Release();
        }
    }

    private async Task AwaitInFlightAsync(CancellationToken ct)
    {
        var tasks = _inFlight.Values.ToArray();
        if (tasks.Length == 0)
        {
            return;
        }

        await Task.WhenAll(tasks).WaitAsync(ct).ConfigureAwait(false);
    }
}
