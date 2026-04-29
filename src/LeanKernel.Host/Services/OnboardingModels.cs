using LeanKernel.Core.Configuration;

namespace LeanKernel.Host.Services;

public sealed class LeanKernelHostPaths
{
    public required string DataDirectory { get; init; }
    public required string RuntimeConfigPath { get; init; }
    public required string OnboardingStatePath { get; init; }
}

public sealed class OnboardingStatus
{
    public bool Completed { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class OnboardingStateDocument
{
    public bool Completed { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Version { get; init; } = "1";
}

public sealed class OnboardingValidationResult
{
    public List<OnboardingStepResult> Steps { get; init; } = [];
    public bool Success => Steps.All(s => s.Success);
}

public sealed class OnboardingStepResult
{
    public required string Step { get; init; }
    public required bool Success { get; init; }
    public required string Message { get; init; }
}

public sealed class OnboardingCompletionResult
{
    public bool Success { get; init; }
    public required string Message { get; init; }
    public required OnboardingStatus Status { get; init; }
    public required OnboardingValidationResult Validation { get; init; }
}

public sealed class OnboardingConfigInput
{
    public LiteLlmConfig LiteLlm { get; init; } = new();
    public QdrantConfig Qdrant { get; init; } = new();
    public SignalConfig Signal { get; init; } = new();
    public WikiConfig Wiki { get; init; } = new();
    public SchedulerConfig Scheduler { get; init; } = new();
}

public interface IOnboardingStateStore
{
    Task<OnboardingStateDocument> GetAsync(CancellationToken ct);
    Task<bool> IsCompletedAsync(CancellationToken ct);
    Task MarkInProgressAsync(CancellationToken ct);
    Task MarkCompletedAsync(CancellationToken ct);
}

public interface IRuntimeLeanKernelConfigStore
{
    LeanKernelConfig GetCurrent();
    Task SaveAsync(LeanKernelConfig config, CancellationToken ct);
}

public interface IOnboardingOrchestrator
{
    Task<OnboardingStatus> GetStatusAsync(CancellationToken ct);
    Task<OnboardingConfigInput> GetDraftAsync(CancellationToken ct);
    Task<OnboardingStatus> SaveDraftAsync(OnboardingConfigInput draft, CancellationToken ct);
    Task<OnboardingValidationResult> ValidateAsync(CancellationToken ct);
    Task<OnboardingCompletionResult> CompleteAsync(CancellationToken ct);
}
