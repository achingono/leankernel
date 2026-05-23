namespace LeanKernel.Abstractions.Models;

public sealed record DiagnosticEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public required string SessionId { get; init; }
    public string? TurnId { get; init; }
    public required string Category { get; init; }
    public required object Payload { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
