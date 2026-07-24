namespace LeanKernel.Events;

/// <summary>
/// Event emitted when a conversation turn completes.
/// Carries turn identity metadata and message summaries.
/// </summary>
public sealed record TurnCompletedEvent : IHasEnvelope
{
    /// <summary>
    /// Gets the event envelope with partitioning and correlation metadata.
    /// </summary>
    public required EventEnvelope Envelope { get; init; }

    /// <summary>
    /// Gets the unique turn identifier.
    /// </summary>
    public required Guid TurnId { get; init; }

    /// <summary>
    /// Gets the session identifier this turn belongs to.
    /// </summary>
    public required Guid SessionId { get; init; }

    /// <summary>
    /// Gets the user message text.
    /// </summary>
    public required string UserMessage { get; init; }

    /// <summary>
    /// Gets the assistant response text.
    /// </summary>
    public required string AssistantResponse { get; init; }

    /// <summary>
    /// Gets the list of tool call summaries.
    /// </summary>
    public IReadOnlyList<string> ToolCalls { get; init; } = [];

    /// <summary>
    /// Gets the turn duration in milliseconds.
    /// </summary>
    public long ElapsedMs { get; init; }
}