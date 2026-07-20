using System.Text.Json;

using LeanKernel.Services.Common.Contracts;
using LeanKernel.Services.Common.Scheduler;
using LeanKernel.Services.Learning.Learning;

namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Detects onboarding gaps and publishes directives for a completed turn payload.
/// </summary>
/// <param name="gapDetector">Detector used to find missing onboarding data.</param>
/// <param name="directiveBuilder">Builder used to create onboarding directives.</param>
/// <param name="directivePublisher">Publisher used to persist directives.</param>
/// <param name="logger">Logger instance.</param>
public sealed class OnboardingGapDetectionScheduledJobHandler(
    IOnboardingGapDetector gapDetector,
    IOnboardingDirectiveBuilder directiveBuilder,
    IOnboardingDirectivePublisher directivePublisher,
    ILogger<OnboardingGapDetectionScheduledJobHandler> logger) : IScheduledJobHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public string JobType => ScheduledJobTypes.OnboardingDetectGaps;

    /// <inheritdoc />
    public async Task ExecuteAsync(ScheduledJobDefinition job, JsonElement? payload, CancellationToken cancellationToken = default)
    {
        if (!payload.HasValue || payload.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"Job '{job.Name}' ({JobType}) requires a completed-turn payload.");
        }

        var turnEvent = payload.Value.Deserialize<CompletedTurnEvent>(SerializerOptions);
        if (turnEvent is null)
        {
            throw new InvalidOperationException($"Job '{job.Name}' ({JobType}) payload could not be parsed as a completed turn event.");
        }

        var gaps = gapDetector.DetectGaps(turnEvent);
        if (gaps.Count == 0)
        {
            logger.LogInformation("No onboarding gaps detected for scheduled job {JobName} turn {TurnId}.", job.Name, turnEvent.TurnId);
            return;
        }

        var directive = directiveBuilder.BuildDirective(turnEvent, gaps);
        await directivePublisher.PublishAsync(turnEvent, directive, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Published onboarding directive from scheduled job {JobName} for turn {TurnId}.", job.Name, turnEvent.TurnId);
    }
}
    /// <inheritdoc />
