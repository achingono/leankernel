using LeanKernel.Core.Enums;

namespace LeanKernel.Core.Models;

/// <summary>
/// A 5W1H fact record persisted to the wiki filesystem.
/// </summary>
public sealed record WikiEntry
{
    public required string Id { get; init; }
    public required WikiDimension Dimension { get; init; }
    public required string Subject { get; init; }
    public List<WikiFact> Facts { get; init; } = [];
    public List<string> Relations { get; init; } = [];
    public DateTimeOffset LastAccessed { get; set; } = DateTimeOffset.UtcNow;
    public int AccessCount { get; set; }
}

public sealed record WikiFact
{
    public required string Claim { get; init; }
    public double Confidence { get; set; } = 0.5;
    public string? Source { get; init; }
    public DateTimeOffset LastConfirmed { get; set; } = DateTimeOffset.UtcNow;
    public int EstimatedTokens { get; set; }
}
