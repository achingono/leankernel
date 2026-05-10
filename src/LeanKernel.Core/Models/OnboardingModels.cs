using LeanKernel.Core.Configuration;

namespace LeanKernel.Core.Models;

/// <summary>
/// Current onboarding completion status.
/// </summary>
public sealed class OnboardingStatus
{
    /// <summary>Gets whether onboarding is complete.</summary>
    public bool Completed { get; init; }

    /// <summary>Gets when onboarding completed.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Gets when onboarding state was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Persisted onboarding state document.
/// </summary>
public sealed class OnboardingStateDocument
{
    /// <summary>Gets whether onboarding is complete.</summary>
    public bool Completed { get; init; }

    /// <summary>Gets when onboarding completed.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Gets when onboarding state was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the state document schema version.</summary>
    public string Version { get; init; } = "1";
}

/// <summary>
/// Aggregate onboarding validation result.
/// </summary>
public sealed class OnboardingValidationResult
{
    /// <summary>Gets individual validation steps.</summary>
    public List<OnboardingStepResult> Steps { get; init; } = [];

    /// <summary>Gets whether all validation steps succeeded.</summary>
    public bool Success => Steps.All(s => s.Success);
}

/// <summary>
/// Result for one onboarding validation step.
/// </summary>
public sealed class OnboardingStepResult
{
    /// <summary>Gets the stable step name.</summary>
    public required string Step { get; init; }

    /// <summary>Gets whether the step succeeded.</summary>
    public required bool Success { get; init; }

    /// <summary>Gets the step result message.</summary>
    public required string Message { get; init; }
}

/// <summary>
/// Result for onboarding completion.
/// </summary>
public sealed class OnboardingCompletionResult
{
    /// <summary>Gets whether completion succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Gets the completion message.</summary>
    public required string Message { get; init; }

    /// <summary>Gets the resulting onboarding status.</summary>
    public required OnboardingStatus Status { get; init; }

    /// <summary>Gets the validation result used for completion.</summary>
    public required OnboardingValidationResult Validation { get; init; }
}

/// <summary>
/// Result of a SELF.md or USER.md configuration step.
/// </summary>
public sealed class ConfigurationStepResult
{
    /// <summary>Gets whether the operation succeeded.</summary>
    public required bool Success { get; init; }

    /// <summary>Gets a user-readable result message.</summary>
    public required string Message { get; init; }

    /// <summary>Gets whether the target file already existed.</summary>
    public bool AlreadyExists { get; init; }

    /// <summary>Gets validation status when this result came from validation.</summary>
    public bool? IsValid { get; init; }

    /// <summary>Gets the related file path.</summary>
    public string? FilePath { get; init; }

    /// <summary>Gets validation errors.</summary>
    public List<string> Errors { get; init; } = [];

    /// <summary>Gets validation warnings.</summary>
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// Mutable onboarding configuration draft.
/// </summary>
public sealed class OnboardingConfigInput
{
    /// <summary>Gets LiteLLM draft settings.</summary>
    public LiteLlmConfig LiteLlm { get; init; } = new();

    /// <summary>Gets Qdrant draft settings.</summary>
    public QdrantConfig Qdrant { get; init; } = new();

    /// <summary>Gets Signal draft settings.</summary>
    public SignalConfig Signal { get; init; } = new();

    /// <summary>Gets wiki draft settings.</summary>
    public WikiConfig Wiki { get; init; } = new();

    /// <summary>Gets scheduler draft settings.</summary>
    public SchedulerConfig Scheduler { get; init; } = new();
}
