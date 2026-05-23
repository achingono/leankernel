namespace LeanKernel.Abstractions.Models;

public sealed record TurnEvent
{
    public required string SessionId { get; init; }
    public required string TurnId { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public required string UserMessage { get; init; }
    public required string AssistantResponse { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public ConversationContext? Context { get; init; }
    public string? ModelUsed { get; init; }
    public RoutingDecision? RoutingDecision { get; init; }
    public OrchestrationResult? OrchestrationResult { get; init; }
    public int? TokensUsed { get; init; }
}
