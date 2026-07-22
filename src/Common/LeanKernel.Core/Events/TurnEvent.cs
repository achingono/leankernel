namespace LeanKernel.Events;

/// <summary>
/// Represents a single turn in a conversation, emitted as an append-only event.
/// Coexists with <see cref="LeanKernel.Entities.TurnEntity"/> during migration;
/// future consumers may read from the event spine directly.
/// </summary>
public sealed record TurnEvent
{
    /// <summary>
    /// Gets the event envelope providing partitioning and correlation metadata.
    /// </summary>
    public required EventEnvelope Envelope { get; init; }

    /// <summary>
    /// Gets the message role (e.g. "user", "assistant", "system", "tool").
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// Gets the text content of the turn.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets the optional author name for the message.
    /// </summary>
    public string? AuthorName { get; init; }

    /// <summary>
    /// Gets the session identifier this turn belongs to.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the optional conversation identifier for multi-turn continuity.
    /// </summary>
    public string? ConversationId { get; init; }

    /// <summary>
    /// Gets the idempotency key for deduplication on replay.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}