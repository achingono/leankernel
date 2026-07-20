namespace LeanKernel.Logic.Memory;

/// <summary>
/// Represents a compact evidence payload supplied to the reasoning model.
/// </summary>
public sealed record RelatedEvidencePage(
    string TargetKey,
    string Snippet,
    IReadOnlyList<string> Reasons,
    int Score,
    double Similarity);