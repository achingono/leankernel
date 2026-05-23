namespace LeanKernel.Abstractions.Models;

public sealed record QualityEvaluationContext
{
    public required string UserMessage { get; init; }

    public required string Response { get; init; }

    public int MinOutputLength { get; init; }

    public double MinConstraintCoverage { get; init; }

    public IReadOnlyList<string>? ExpectedConstraints { get; init; }
}
