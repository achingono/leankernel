namespace LeanKernel.Logic.Memory;

/// <summary>
/// Represents a parsed memory page and its derived metadata.
/// </summary>
public sealed record MemoryPageSnapshot(
    string Key,
    string Content,
    string FactText,
    string NormalizedFactText,
    DateTimeOffset EffectiveTimestamp,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyDictionary<string, string?> Fields,
    string? SessionId,
    string? TurnId,
    IReadOnlyList<string> ExplicitLinks,
    string? SupersededBy,
    string PrimaryDimension,
    IReadOnlyList<string> SecondaryDimensions,
    IReadOnlyList<MemoryPageLink> Links,
    bool IsRetired = false);
