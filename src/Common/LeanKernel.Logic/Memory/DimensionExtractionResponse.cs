namespace LeanKernel.Logic.Memory;

/// <summary>
/// Represents a model response containing extracted memory dimensions.
/// </summary>
public sealed record DimensionExtractionResponse(
    string PrimaryDimension,
    IReadOnlyList<string> SecondaryDimensions,
    IReadOnlyDictionary<string, string> DimensionRationales,
    IReadOnlyDictionary<string, IReadOnlyList<string>> NormalizedDimensionValues);