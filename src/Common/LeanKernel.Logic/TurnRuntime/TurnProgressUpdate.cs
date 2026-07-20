namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// A progress update for a long-running turn.
/// </summary>
public sealed class TurnProgressUpdate
{
    /// <summary>
    /// The conversation this update belongs to.
    /// </summary>
    public required string ConversationId { get; init; }

    /// <summary>
    /// Human-readable status message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Current stage name.
    /// </summary>
    public string? Stage { get; init; }

    /// <summary>
    /// Progress percentage (0-100), if known.
    /// </summary>
    public int? PercentComplete { get; init; }

    /// <summary>
    /// UTC timestamp of the update.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}