namespace LeanKernel.Logic.Memory;

/// <summary>
/// Represents a candidate evidence page considered during relatedness scoring.
/// </summary>
internal sealed record RelatedEvidenceCandidate(
    MemoryPageSnapshot Page,
    int Score,
    IReadOnlyList<string> Reasons,
    double Similarity);