namespace LeanKernel.Entities;

using System;

/// <summary>
/// Represents a single captured diagnostic event within the system.
/// </summary>
public sealed class DiagnosticEntry : IEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for this diagnostic entry.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets an optional correlation identifier used to group related entries.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets an optional turn identifier associated with this entry.
    /// </summary>
    public Guid? TurnId { get; set; }

    /// <summary>
    /// Gets or sets the component or subsystem that emitted this diagnostic entry.
    /// </summary>
    public string Source { get; set; } = null!;

    /// <summary>
    /// Gets or sets the category or type of this diagnostic entry.
    /// </summary>
    public string Category { get; set; } = null!;

    /// <summary>
    /// Gets or sets the JSON-serialized payload containing diagnostic details.
    /// </summary>
    public string PayloadJson { get; set; } = null!;

    /// <summary>
    /// Gets or sets the UTC timestamp when this entry was captured.
    /// </summary>
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}
