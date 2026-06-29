namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents a single turn in a conversation as a data event.
/// </summary>
public sealed record TurnEvent
{
    /// <summary>
    /// Gets the session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets the turn identifier.
    /// </summary>
    public required string TurnId { get; init; }

    /// <summary>
    /// Gets the role of the participant.
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// Gets the content of the turn.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets the user's message in the turn.
    /// </summary>
    public required string UserMessage { get; init; }

    /// <summary>
    /// Gets the assistant's response in the turn.
    /// </summary>
    public required string AssistantResponse { get; init; }

    /// <summary>
    /// Gets the timestamp when the turn occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the conversation context at the time of the turn, if any.
    /// </summary>
    public ConversationContext? Context { get; init; }

    /// <summary>
    /// Gets the model used for the turn, if any.
    /// </summary>
    public string? ModelUsed { get; init; }

    /// <summary>
    /// Gets the routing decision made for the turn, if any.
    /// </summary>
    public RoutingDecision? RoutingDecision { get; init; }

    /// <summary>
    /// Gets the orchestration result for the turn, if any.
    /// </summary>
    public OrchestrationResult? OrchestrationResult { get; init; }

    /// <summary>
    /// Gets the number of tokens used for the turn, if available.
    /// </summary>
    public int? TokensUsed { get; init; }
}
