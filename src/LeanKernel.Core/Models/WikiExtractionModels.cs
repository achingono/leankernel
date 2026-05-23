namespace LeanKernel.Core.Models;

/// <summary>
/// Root extraction response contract from the LLM.
/// </summary>
public sealed record WikiExtractionResponse
{
    /// <summary>
    /// Gets or sets the extracted facts.
    /// </summary>
    public List<ExtractedWikiFact> Facts { get; init; } = [];
}

/// <summary>
/// Parsed DTO returned by LLM extraction before mapping into canonical wiki entries.
/// </summary>
public sealed record ExtractedWikiFact
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
    /// <summary>
    /// Gets or sets the claim.
    /// </summary>
    public required string Claim { get; init; }
    /// <summary>
    /// Gets or sets the subject.
    /// </summary>
    public required string Subject { get; init; }
    /// <summary>
    /// Gets or sets the primary dimension.
    /// </summary>
    public required string PrimaryDimension { get; init; }
    /// <summary>
    /// Gets or sets the source quote.
    /// </summary>
    public string? SourceQuote { get; init; }
    /// <summary>
    /// Gets or sets the summary hint.
    /// </summary>
    public string? SummaryHint { get; init; }
    /// <summary>
    /// Gets or sets aliases.
    /// </summary>
    public List<string> Aliases { get; init; } = [];
    /// <summary>
    /// Gets or sets tags.
    /// </summary>
    public List<string> Tags { get; init; } = [];
}
