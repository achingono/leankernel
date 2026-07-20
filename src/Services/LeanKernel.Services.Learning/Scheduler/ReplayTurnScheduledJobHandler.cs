using System.Text.Json;

using LeanKernel.Services.Common.Contracts;
using LeanKernel.Services.Common.Scheduler;
using LeanKernel.Services.Learning.Learning;

namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Replays a completed turn payload through the full learning pipeline.
/// </summary>
/// <param name="pipeline">Learning pipeline used to process replayed turns.</param>
/// <param name="logger">Logger instance.</param>
public sealed class ReplayTurnScheduledJobHandler(
    ISelfImprovementPipeline pipeline,
    ILogger<ReplayTurnScheduledJobHandler> logger) : IScheduledJobHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public string JobType => "learning.replay-turn";

    public async Task ExecuteAsync(ScheduledJobDefinition job, JsonElement? payload, CancellationToken cancellationToken = default)
    {
        if (!payload.HasValue || payload.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"Job '{job.Name}' ({JobType}) requires a JSON payload describing a completed turn event.");
        }

        var completedTurn = payload.Value.Deserialize<CompletedTurnEvent>(SerializerOptions);
        if (completedTurn is null)
        {
            throw new InvalidOperationException($"Job '{job.Name}' ({JobType}) payload could not be deserialized as a completed turn event.");
        }

        await pipeline.ExecuteAsync(completedTurn, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Replayed scheduled learning turn {TurnId} from job {JobName}.", completedTurn.TurnId, job.Name);
    }
}
    public string JobType => ScheduledJobTypes.LearningReplayTurn;
    /// <inheritdoc />
