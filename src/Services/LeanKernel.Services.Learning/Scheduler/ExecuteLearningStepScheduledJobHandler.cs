using System.Text.Json;

using LeanKernel.Services.Common.Contracts;
using LeanKernel.Services.Common.Scheduler;
using LeanKernel.Services.Learning.Learning;

namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Executes a single named learning step for the supplied turn payload.
/// </summary>
/// <param name="stepRunner">Runner used to execute one learning step.</param>
/// <param name="logger">Logger instance.</param>
public sealed class ExecuteLearningStepScheduledJobHandler(
    ILearningStepRunner stepRunner,
    ILogger<ExecuteLearningStepScheduledJobHandler> logger) : IScheduledJobHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public string JobType => ScheduledJobTypes.LearningExecuteStep;

    /// <inheritdoc />
    public async Task ExecuteAsync(ScheduledJobDefinition job, JsonElement? payload, CancellationToken cancellationToken = default)
    {
        if (!payload.HasValue || payload.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"Job '{job.Name}' ({JobType}) requires a JSON payload.");
        }

        var request = payload.Value.Deserialize<ExecuteLearningStepPayload>(SerializerOptions);
        if (request is null)
        {
            throw new InvalidOperationException($"Job '{job.Name}' ({JobType}) payload could not be parsed.");
        }

        await stepRunner.ExecuteStepAsync(request.StepName, request.TurnEvent, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Executed learning step {StepName} from scheduled job {JobName}.", request.StepName, job.Name);
    }

    private sealed record ExecuteLearningStepPayload(string StepName, CompletedTurnEvent TurnEvent);
}
    /// <inheritdoc />
