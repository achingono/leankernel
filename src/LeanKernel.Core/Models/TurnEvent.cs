namespace LeanKernel.Core.Models;

/// <summary>
/// Immutable event emitted after a user turn has been processed.
/// </summary>
public sealed record TurnEvent
{
    /// <summary>
    /// Gets the event identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the conversation session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets the inbound message that started the turn.
    /// </summary>
    public required LeanKernelMessage UserMessage { get; init; }

    /// <summary>
    /// Gets the assistant response generated for the turn.
    /// </summary>
    public required string AssistantResponse { get; init; }

    /// <summary>
    /// Gets the context selected for the turn.
    /// </summary>
    public ConversationContext? Context { get; init; }

    /// <summary>
    /// Gets the source identifier used when persisting learning artifacts.
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// Gets when the turn completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets whether the model invocation failed.
    /// </summary>
    public bool HasFailure => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// <summary>
    /// Gets the failure type, when known.
    /// </summary>
    public string? ErrorType { get; init; }

    /// <summary>
    /// Gets the failure message, when the turn completed through a fallback path.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result of running a learning step for a turn event.
/// </summary>
public sealed record LearningStepResult
{
    /// <summary>
    /// Gets whether the learning step succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the learning step name.
    /// </summary>
    public required string StepName { get; init; }

    /// <summary>
    /// Gets an optional result message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets whether the failed step can be retried.
    /// </summary>
    public bool IsRetryable { get; init; }

    /// <summary>
    /// Creates a successful step result.
    /// </summary>
    /// <param name="stepName">The learning step name.</param>
    /// <param name="message">The optional success message.</param>
    /// <returns>A successful learning step result.</returns>
    public static LearningStepResult Succeeded(string stepName, string? message = null) =>
        new()
        {
            Success = true,
            StepName = stepName,
            Message = message
        };

    /// <summary>
    /// Creates a failed step result.
    /// </summary>
    /// <param name="stepName">The learning step name.</param>
    /// <param name="message">The failure message.</param>
    /// <param name="isRetryable">Whether the failure can be retried.</param>
    /// <returns>A failed learning step result.</returns>
    public static LearningStepResult Failed(string stepName, string message, bool isRetryable = true) =>
        new()
        {
            Success = false,
            StepName = stepName,
            Message = message,
            IsRetryable = isRetryable
        };
}

/// <summary>
/// Aggregate result for a self-improvement pipeline execution.
/// </summary>
public sealed record SelfImprovementResult
{
    /// <summary>
    /// Gets the event identifier processed by the pipeline.
    /// </summary>
    public required string TurnEventId { get; init; }

    /// <summary>
    /// Gets the individual learning step results.
    /// </summary>
    public required IReadOnlyList<LearningStepResult> StepResults { get; init; }

    /// <summary>
    /// Gets whether every learning step succeeded.
    /// </summary>
    public bool Success => StepResults.All(result => result.Success);
}
