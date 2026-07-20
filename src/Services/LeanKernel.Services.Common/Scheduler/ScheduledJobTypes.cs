namespace LeanKernel.Services.Common.Scheduler;

/// <summary>
/// Known scheduled job type identifiers.
/// </summary>
public static class ScheduledJobTypes
{
    /// <summary>
    /// Heartbeat diagnostic job.
    /// </summary>
    public const string LearningPing = "learning.ping";
    /// <summary>
    /// Replays a completed turn through the full learning pipeline.
    /// </summary>
    public const string LearningReplayTurn = "learning.replay-turn";
    /// <summary>
    /// Executes one specific learning step.
    /// </summary>
    public const string LearningExecuteStep = "learning.execute-step";
    /// <summary>
    /// Detects onboarding gaps and emits directives.
    /// </summary>
    public const string OnboardingDetectGaps = "onboarding.detect-gaps";
}
