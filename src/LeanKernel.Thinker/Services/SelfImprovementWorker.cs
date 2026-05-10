using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Thinker.Resources;

namespace LeanKernel.Thinker.Services;

/// <summary>
/// Drains queued turn events through the self-improvement pipeline.
/// </summary>
public sealed class SelfImprovementWorker : BackgroundService
{
    private readonly TurnEventQueue _queue;
    private readonly ISelfImprovementPipeline _pipeline;
    private readonly ILogger<SelfImprovementWorker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SelfImprovementWorker" /> class.
    /// </summary>
    /// <param name="queue">The durable turn-event queue.</param>
    /// <param name="pipeline">The self-improvement pipeline to execute.</param>
    /// <param name="logger">The logger used for worker diagnostics.</param>
    public SelfImprovementWorker(
        TurnEventQueue queue,
        ISelfImprovementPipeline pipeline,
        ILogger<SelfImprovementWorker> logger)
    {
        _queue = queue;
        _pipeline = pipeline;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _queue.RestorePendingAsync(stoppingToken);

        await foreach (var turnEvent in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                var result = await _pipeline.ProcessAsync(turnEvent, stoppingToken);
                if (result.Success)
                {
                    await _queue.MarkProcessedAsync(turnEvent.Id, stoppingToken);
                    continue;
                }

                _logger.LogWarning(
                    ResourceText.Log("SelfImprovementPipelineCompletedWithFailures"),
                    turnEvent.Id,
                    string.Join(", ", result.StepResults.Where(r => !r.Success).Select(r => r.StepName)));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ResourceText.Log("SelfImprovementWorkerFailed"), turnEvent.Id);
            }
        }
    }
}
