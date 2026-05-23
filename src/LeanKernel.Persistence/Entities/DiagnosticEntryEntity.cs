namespace LeanKernel.Persistence.Entities;

/// <summary>
/// Represents a persisted diagnostic event for a session or turn.
/// </summary>
public sealed class DiagnosticEntryEntity
{
    /// <summary>
    /// Gets or sets the unique diagnostic entry identifier.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the related session identifier.
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the related turn identifier when available.
    /// </summary>
    public string? TurnId { get; set; }

    /// <summary>
    /// Gets or sets the diagnostic category.
    /// </summary>
    public required string Category { get; set; }

    /// <summary>
    /// Gets or sets the serialized diagnostic payload.
    /// </summary>
    public required string Payload { get; set; }

    /// <summary>
    /// Gets or sets when the diagnostic entry was recorded.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
