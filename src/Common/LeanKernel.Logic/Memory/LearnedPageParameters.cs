namespace LeanKernel.Logic.Memory;

/// <summary>
/// Parameters for rendering a learned memory page.
/// </summary>
public sealed record LearnedPageParameters(
    IReadOnlyDictionary<string, string?> Fields,
    string PrimaryDimension,
    IReadOnlyList<string> SecondaryDimensions,
    IReadOnlyList<MemoryPageLink> Links,
    string NormalizationStatus,
    string NormalizationMethod,
    IReadOnlyList<string> MissingFields,
    string? Session,
    string? Turn,
    DateTimeOffset? RecordedAt);