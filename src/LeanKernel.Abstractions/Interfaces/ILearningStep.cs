using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

public interface ILearningStep
{
    string Name { get; }

    int Order { get; }

    Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct = default);
}

public sealed record LearningStepResult
{
    public required string StepName { get; init; }

    public bool Success { get; init; }

    public int ItemsLearned { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<string> LearnedFacts { get; init; } = [];
}
