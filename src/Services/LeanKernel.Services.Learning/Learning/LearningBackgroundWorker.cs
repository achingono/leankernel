using LeanKernel.Services.Common.Queue;
using LeanKernel.Services.Learning.Configuration;

using Microsoft.Extensions.Options;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Background service that drains completed turns and runs the learning pipeline.
/// </summary>
/// <param name="serviceScopeFactory">Creates per-turn service scopes.</param>
/// <param name="queue">Queue containing completed turn events.</param>
/// <param name="options">Runtime worker options.</param>
/// <param name="logger">Logger instance.</param>
public sealed class LearningBackgroundWorker(
    IServiceScopeFactory serviceScopeFactory,
    ITurnEventQueue queue,
    IOptions<LearningRuntimeOptions> options,
    ILogger<LearningBackgroundWorker> logger) : BackgroundService
{
    private readonly LearningRuntimeOptions _options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Learning background worker is disabled by configuration.");
            return;
        }

        await foreach (var turnEvent in queue.DequeueAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<ISelfImprovementPipeline>();
                await pipeline.ExecuteAsync(turnEvent, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Learning pipeline failed for turn {TurnId}.", turnEvent.TurnId);
            }
        }
    }
}
