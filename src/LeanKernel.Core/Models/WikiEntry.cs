using LeanKernel.Core.Enums;

namespace LeanKernel.Core.Models;

/// <summary>
/// A 5W1H fact record persisted to the wiki filesystem.
/// </summary>
public sealed record WikiEntry
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Gets or sets the dimension.
    /// </summary>
    public required WikiDimension Dimension { get; init; }
    /// <summary>
    /// Gets or sets the subject.
    /// </summary>
    public required string Subject { get; init; }
    /// <summary>
    /// Gets or sets the facts.
    /// </summary>
    public List<WikiFact> Facts { get; init; } = [];
    /// <summary>
    /// Gets or sets the relations.
    /// </summary>
    public List<string> Relations { get; init; } = [];
    /// <summary>
    /// Gets or sets the last accessed.
    /// </summary>
    public DateTimeOffset LastAccessed { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the access count.
    /// </summary>
    public int AccessCount { get; set; }
}

/// <summary>
/// Represents the wiki fact.
/// </summary>
public sealed record WikiFact
{
    /// <summary>
    /// Gets or sets the claim.
    /// </summary>
    public required string Claim { get; init; }
    /// <summary>
    /// Gets or sets the confidence.
    /// </summary>
    public double Confidence { get; set; } = 0.5;
    /// <summary>
    /// Gets or sets the source.
    /// </summary>
    public string? Source { get; init; }
    /// <summary>
    /// Gets or sets the last confirmed.
    /// </summary>
    public DateTimeOffset LastConfirmed { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the estimated tokens.
    /// </summary>
    public int EstimatedTokens { get; set; }
}
