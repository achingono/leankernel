namespace LeanKernel.Logic.Memory;

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