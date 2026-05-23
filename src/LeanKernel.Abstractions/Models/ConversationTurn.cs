namespace LeanKernel.Abstractions.Models;

public sealed record ConversationTurn
{
    public required string Role { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? TurnId { get; init; }
    public bool IsCompacted { get; init; }
    public string? CompactionSourceId { get; init; }
}
