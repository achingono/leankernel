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
    /// Gets or sets the summary.
    /// </summary>
    public string? Summary { get; init; }
    /// <summary>
    /// Gets or sets the aliases.
    /// </summary>
    public List<string> Aliases { get; init; } = [];
    /// <summary>
    /// Gets or sets the tags.
    /// </summary>
    public List<string> Tags { get; init; } = [];
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
    /// Gets or sets the context.
    /// </summary>
    public WikiFactContext? Context { get; init; }
    /// <summary>
    /// Gets or sets the source quote.
    /// </summary>
    public string? SourceQuote { get; init; }
    /// <summary>
    /// Gets or sets the normalized key.
    /// </summary>
    public string? NormalizedKey { get; init; }
    /// <summary>
    /// Gets or sets the tags.
    /// </summary>
    public List<string> Tags { get; init; } = [];
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

/// <summary>
/// Represents structured 5W1H context fields for a single wiki fact.
/// </summary>
public sealed record WikiFactContext
{
    /// <summary>
    /// Gets or sets who.
    /// </summary>
    public string? Who { get; init; }
    /// <summary>
    /// Gets or sets what.
    /// </summary>
    public string? What { get; init; }
    /// <summary>
    /// Gets or sets when.
    /// </summary>
    public string? When { get; init; }
    /// <summary>
    /// Gets or sets where.
    /// </summary>
    public string? Where { get; init; }
    /// <summary>
    /// Gets or sets why.
    /// </summary>
    public string? Why { get; init; }
    /// <summary>
    /// Gets or sets how.
    /// </summary>
    public string? How { get; init; }
}
