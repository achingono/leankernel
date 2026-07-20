namespace LeanKernel.Logic.Memory;

/// <summary>
/// Represents the outcome of normalizing a memory page.
/// </summary>
public sealed record MemoryPageNormalizationResult(
    string Content,
    IReadOnlyDictionary<string, string?> Fields,
    IReadOnlyList<string> MissingFields,
    string PrimaryDimension,
    IReadOnlyList<string> SecondaryDimensions,
    IReadOnlyList<MemoryDimensionScore> Dimensions,
    IReadOnlyList<MemoryPageLink> Links,
    string NormalizationMethod,
    string ScopeRelativeKey)
{
    /// <summary>
    /// Gets a value indicating whether the normalized page is missing any 5W1H fields.
    /// </summary>
    public bool IsPartial => MissingFields.Count > 0;
}
