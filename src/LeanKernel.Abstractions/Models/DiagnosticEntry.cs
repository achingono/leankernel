namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents a single diagnostic entry.
/// </summary>
public sealed record DiagnosticEntry
{
    /// <summary>
    /// Gets the unique identifier for the diagnostic entry.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets the session identifier the diagnostic belongs to.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets the turn identifier associated with the diagnostic, if any.
    /// </summary>
    public string? TurnId { get; init; }

    /// <summary>
    /// Gets the category of the diagnostic.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Gets the payload of the diagnostic.
    /// </summary>
    public required object Payload { get; init; }

    /// <summary>
    /// Gets the timestamp when the diagnostic was recorded.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
