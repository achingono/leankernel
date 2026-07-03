namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents a per-turn progress update published while work is in flight.
/// </summary>
public sealed record TurnProgressUpdate(
    string SessionId,
    string TurnId,
    TurnProgressKind Kind,
    string? ToolName,
    string? Message,
    DateTimeOffset Timestamp);

/// <summary>
/// Enumerates progress event types emitted during a turn.
/// </summary>
public enum TurnProgressKind
{
    ToolStarted,
    ToolCompleted,
    ContinuationStarted,
    StatusNote,
    Heartbeat,
}
