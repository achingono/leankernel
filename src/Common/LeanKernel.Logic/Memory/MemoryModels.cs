namespace LeanKernel.Logic.Memory;

/// <summary>
/// Represents a parsed memory page and its derived metadata.
/// </summary>
public sealed record MemoryPageSnapshot(
    string Key,
    string Content,
    string FactText,
    string NormalizedFactText,
    DateTimeOffset EffectiveTimestamp,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyDictionary<string, string?> Fields,
    string? SessionId,
    string? TurnId,
    IReadOnlyList<string> ExplicitLinks,
    string? SupersededBy,
    string PrimaryDimension,
    IReadOnlyList<string> SecondaryDimensions,
    IReadOnlyList<MemoryPageLink> Links,
    bool IsRetired = false);

/// <summary>
/// Represents the outcome of normalizing a memory page.
/// </summary>
public sealed record MemoryPageNormalizationResult(
    string Content,
    IReadOnlyDictionary<string, string?> Fields,
    IReadOnlyList<string> MissingFields,
    string PrimaryDimension,
    IReadOnlyList<string> SecondaryDimensions,
    IReadOnlyList<MemoryDimensionScore> Dimensions,
    IReadOnlyList<MemoryPageLink> Links,
    string NormalizationMethod,
    string ScopeRelativeKey)
{
    /// <summary>
    /// Gets a value indicating whether the normalized page is missing any 5W1H fields.
    /// </summary>
    public bool IsPartial => MissingFields.Count > 0;
}

/// <summary>
/// Represents a dimension score and the reason it was assigned.
/// </summary>
public sealed record MemoryDimensionScore(string Dimension, int Score, string Reason, string Source = "deterministic");

/// <summary>
/// Represents a related link between two memory pages.
/// </summary>
public sealed record MemoryPageLink(
    string TargetKey,
    string Relation,
    int Score,
    IReadOnlyList<string> Reasons,
    double? Confidence = null,
    string Source = "deterministic");

/// <summary>
/// Represents a candidate evidence page considered during relatedness scoring.
/// </summary>
internal sealed record RelatedEvidenceCandidate(
    MemoryPageSnapshot Page,
    int Score,
    IReadOnlyList<string> Reasons,
    double Similarity);

/// <summary>
/// Represents a compact evidence payload supplied to the reasoning model.
/// </summary>
public sealed record RelatedEvidencePage(
    string TargetKey,
    string Snippet,
    IReadOnlyList<string> Reasons,
    int Score,
    double Similarity);

/// <summary>
/// Represents a request to extract primary and secondary dimensions for a page.
/// </summary>
public sealed record DimensionExtractionRequest(
    string FactText,
    IReadOnlyDictionary<string, string?> Fields,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<RelatedEvidencePage> RelatedEvidence);

/// <summary>
/// Represents a model response containing extracted memory dimensions.
/// </summary>
public sealed record DimensionExtractionResponse(
    string PrimaryDimension,
    IReadOnlyList<string> SecondaryDimensions,
    IReadOnlyDictionary<string, string> DimensionRationales,
    IReadOnlyDictionary<string, IReadOnlyList<string>> NormalizedDimensionValues);

/// <summary>
/// Represents a request to refine memory graph links.
/// </summary>
public sealed record GraphReasoningRequest(
    string FactText,
    IReadOnlyDictionary<string, string?> Fields,
    IReadOnlyList<MemoryPageLink> DeterministicLinks,
    IReadOnlyList<RelatedEvidencePage> CandidatePages);

/// <summary>
/// Represents a model response containing proposed memory graph edges.
/// </summary>
public sealed record GraphReasoningResponse(IReadOnlyList<ProposedEdge> Links);

/// <summary>
/// Represents a single proposed graph edge from the reasoning model.
/// </summary>
public sealed record ProposedEdge(
    string TargetKey,
    string Relation,
    double Confidence,
    IReadOnlyList<string> Reasons);
