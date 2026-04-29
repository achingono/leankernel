using LeanKernel.Core.Enums;

namespace LeanKernel.Core.Models;

/// <summary>
/// A structured query against the 5W1H wiki.
/// </summary>
public sealed class WikiQuery
{
    public string? TextQuery { get; init; }
    public HashSet<WikiDimension> Dimensions { get; init; } = [];
    public int MaxResults { get; init; } = 10;
    public double MinConfidence { get; init; } = 0.5;
    public TimeSpan? MaxAge { get; init; }
}
