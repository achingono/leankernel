namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents the context required for evaluating the quality of an agent response.
/// </summary>
public sealed record QualityEvaluationContext
{
    /// <summary>
    /// Gets the original user message.
    /// </summary>
    public required string UserMessage { get; init; }

    /// <summary>
    /// Gets the agent's response to evaluate.
    /// </summary>
    public required string Response { get; init; }

    /// <summary>
    /// Gets the minimum expected output length.
    /// </summary>
    public int MinOutputLength { get; init; }

    /// <summary>
    /// Gets the minimum fraction of constraints that must be met (0.0 to 1.0).
    /// </summary>
    public double MinConstraintCoverage { get; init; }

    /// <summary>
    /// Gets the list of explicit constraints to verify in the response.
    /// </summary>
    public IReadOnlyList<string>? ExpectedConstraints { get; init; }
}
