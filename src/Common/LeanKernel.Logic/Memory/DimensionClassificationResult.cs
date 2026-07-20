using System.Diagnostics.Metrics;
using System.Text.Json;

namespace LeanKernel.Logic.Memory;

/// <summary>
/// Represents the outcome of classifying a memory page into 5W1H dimensions.
/// </summary>
public sealed record DimensionClassificationResult(
    string PrimaryDimension,
    IReadOnlyList<string> SecondaryDimensions,
    IReadOnlyList<MemoryDimensionScore> DimensionScores,
    string Source);