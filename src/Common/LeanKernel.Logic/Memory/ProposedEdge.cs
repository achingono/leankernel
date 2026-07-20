namespace LeanKernel.Logic.Memory;

/// <summary>
/// Represents a single proposed graph edge from the reasoning model.
/// </summary>
public sealed record ProposedEdge(
    string TargetKey,
    string Relation,
    double Confidence,
    IReadOnlyList<string> Reasons);