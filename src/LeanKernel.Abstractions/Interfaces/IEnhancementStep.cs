using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Represents a deterministic response enhancement step.
/// </summary>
public interface IEnhancementStep
{
    /// <summary>
    /// Gets the stable step name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the step execution order.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Executes the enhancement step.
    /// </summary>
    /// <param name="input">The current enhancement input.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The step output.</returns>
    Task<EnhancementStepOutput> ExecuteAsync(EnhancementStepInput input, CancellationToken ct = default);
}

/// <summary>
/// Represents the input passed to a response enhancement step.
/// </summary>
public sealed record EnhancementStepInput
{
    /// <summary>
    /// Gets the response currently being enhanced.
    /// </summary>
    public required string Response { get; init; }

    /// <summary>
    /// Gets the originating user message.
    /// </summary>
    public required string UserMessage { get; init; }

    /// <summary>
    /// Gets the optional session identifier.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the retrieved knowledge available to the enhancement step.
    /// </summary>
    public IReadOnlyList<RetrievalCandidate>? RetrievedKnowledge { get; init; }
}

/// <summary>
/// Represents the output from a response enhancement step.
/// </summary>
public sealed record EnhancementStepOutput
{
    /// <summary>
    /// Gets the response after the step executes.
    /// </summary>
    public required string Response { get; init; }

    /// <summary>
    /// Gets a value indicating whether the response was modified.
    /// </summary>
    public bool Modified { get; init; }

    /// <summary>
    /// Gets the optional explanation for the step outcome.
    /// </summary>
    public string? Reason { get; init; }
}
