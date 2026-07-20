namespace LeanKernel.Logic.Memory;

/// <summary>
/// Represents a request to refine memory graph links.
/// </summary>
public sealed record GraphReasoningRequest(
    string FactText,
    IReadOnlyDictionary<string, string?> Fields,
    IReadOnlyList<MemoryPageLink> DeterministicLinks,
    IReadOnlyList<RelatedEvidencePage> CandidatePages);