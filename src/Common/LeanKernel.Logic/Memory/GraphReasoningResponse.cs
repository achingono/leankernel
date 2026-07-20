namespace LeanKernel.Logic.Memory;

/// <summary>
/// Represents a model response containing proposed memory graph edges.
/// </summary>
public sealed record GraphReasoningResponse(IReadOnlyList<ProposedEdge> Links);