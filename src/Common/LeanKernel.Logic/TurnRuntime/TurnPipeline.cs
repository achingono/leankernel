using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// Orchestrates the turn pipeline by executing registered <see cref="ITurnStage"/> instances
/// in order. Each stage mutates the <see cref="TurnContext>`. The pipeline measures total
/// execution time and logs stage-level diagnostics.
/// </summary>
public sealed class TurnPipeline(
    IEnumerable<ITurnStage> stages,
    ILogger<TurnPipeline> logger)
{
    private readonly ITurnStage[] _stages = stages.ToArray();

    /// <summary>
    /// Executes all stages in order against the turn context.
    /// </summary>
    public async Task<TurnPipelineResult> ExecuteAsync(
        TurnContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        logger.LogDebug(
            "Turn pipeline starting for conversation {ConversationId} with {StageCount} stages.",
            context.ConversationId, _stages.Length);

        foreach (var stage in _stages)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Turn pipeline cancelled before stage {StageName}.", stage.Name);
                context.TerminationReason = "cancelled";
                break;
            }

            var stageStopwatch = Stopwatch.StartNew();
            try
            {
                await stage.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                stageStopwatch.Stop();

                logger.LogDebug(
                    "Stage {StageName} completed in {ElapsedMs}ms.",
                    stage.Name, stageStopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stageStopwatch.Stop();
                logger.LogError(ex,
                    "Stage {StageName} failed after {ElapsedMs}ms.",
                    stage.Name, stageStopwatch.ElapsedMilliseconds);

                context.TerminationReason = $"stage_failure:{stage.Name}";
                break;
            }
        }

        stopwatch.Stop();
        context.Elapsed = stopwatch.Elapsed;

        var admitted = context.Admitted.Count;
        var rejected = context.Candidates.Count - admitted;

        logger.LogInformation(
            "Turn pipeline completed for {ConversationId} in {ElapsedMs}ms. " +
            "Admitted: {Admitted}, Rejected: {Rejected}, Continuation: {Continuation}",
            context.ConversationId, stopwatch.ElapsedMilliseconds,
            admitted, rejected, context.RequiresContinuation);

        return new TurnPipelineResult
        {
            Response = context.AgentResponse,
            Elapsed = context.Elapsed,
            AdmittedCount = admitted,
            RejectedCount = rejected,
            RequiresContinuation = context.RequiresContinuation,
            AdmissionTrace = context.AdmissionTrace.AsReadOnly(),
        };
    }
}
