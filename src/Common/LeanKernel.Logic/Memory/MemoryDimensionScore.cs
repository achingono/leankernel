namespace LeanKernel.Logic.Memory;

/// <summary>
/// Represents a dimension score and the reason it was assigned.
/// </summary>
public sealed record MemoryDimensionScore(string Dimension, int Score, string Reason, string Source = "deterministic");
