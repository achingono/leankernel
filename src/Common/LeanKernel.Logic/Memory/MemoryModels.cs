namespace LeanKernel.Logic.Memory;

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
    public bool IsPartial => MissingFields.Count > 0;
}

public sealed record MemoryDimensionScore(string Dimension, int Score, string Reason, string Source = "deterministic");

public sealed record MemoryPageLink(
    string TargetKey,
    string Relation,
    int Score,
    IReadOnlyList<string> Reasons,
    double? Confidence = null,
    string Source = "deterministic");

internal sealed record RelatedEvidenceCandidate(
    MemoryPageSnapshot Page,
    int Score,
    IReadOnlyList<string> Reasons,
    double Similarity);

public sealed record RelatedEvidencePage(
    string TargetKey,
    string Snippet,
    IReadOnlyList<string> Reasons,
    int Score,
    double Similarity);

public sealed record DimensionExtractionRequest(
    string FactText,
    IReadOnlyDictionary<string, string?> Fields,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<RelatedEvidencePage> RelatedEvidence);

public sealed record DimensionExtractionResponse(
    string PrimaryDimension,
    IReadOnlyList<string> SecondaryDimensions,
    IReadOnlyDictionary<string, string> DimensionRationales,
    IReadOnlyDictionary<string, IReadOnlyList<string>> NormalizedDimensionValues);

public sealed record GraphReasoningRequest(
    string FactText,
    IReadOnlyDictionary<string, string?> Fields,
    IReadOnlyList<MemoryPageLink> DeterministicLinks,
    IReadOnlyList<RelatedEvidencePage> CandidatePages);

public sealed record GraphReasoningResponse(IReadOnlyList<ProposedEdge> Links);

public sealed record ProposedEdge(
    string TargetKey,
    string Relation,
    double Confidence,
    IReadOnlyList<string> Reasons);
