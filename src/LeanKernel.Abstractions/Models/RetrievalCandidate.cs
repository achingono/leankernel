namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents a candidate retrieved from a knowledge source.
/// </summary>
public sealed record RetrievalCandidate
{
    /// <summary>
    /// Gets the unique key for the candidate.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the content of the candidate.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets the source of the candidate.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets the relevance score for the candidate.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Gets the token count of the candidate.
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// Gets the metadata associated with the candidate.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
