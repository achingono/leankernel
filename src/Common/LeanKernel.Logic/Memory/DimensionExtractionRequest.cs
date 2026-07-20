namespace LeanKernel.Logic.Memory;

/// <summary>
/// Represents a request to extract primary and secondary dimensions for a page.
/// </summary>
public sealed record DimensionExtractionRequest(
    string FactText,
    IReadOnlyDictionary<string, string?> Fields,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<RelatedEvidencePage> RelatedEvidence);
