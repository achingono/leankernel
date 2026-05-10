using LeanKernel.Core.Enums;

namespace LeanKernel.Core.Models;

/// <summary>
/// A structured query against the 5W1H wiki.
/// </summary>
public sealed class WikiQuery
{
    /// <summary>
    /// Gets or sets the text query.
    /// </summary>
    public string? TextQuery { get; init; }
    /// <summary>
    /// Gets or sets the dimensions.
    /// </summary>
    public HashSet<WikiDimension> Dimensions { get; init; } = [];
    /// <summary>
    /// Gets or sets the max results.
    /// </summary>
    public int MaxResults { get; init; } = 10;
    /// <summary>
    /// Gets or sets the min confidence.
    /// </summary>
    public double MinConfidence { get; init; } = 0.5;
    /// <summary>
    /// Gets or sets the max age.
    /// </summary>
    public TimeSpan? MaxAge { get; init; }
}
