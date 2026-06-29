namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents a record of an item admitted into the context.
/// </summary>
public sealed record ContextAdmissionRecord
{
    /// <summary>
    /// Gets the unique key of the admitted item.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the source of the admitted item.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets the relevance score of the item.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Gets the token count of the item.
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// Gets a value indicating whether the item was admitted.
    /// </summary>
    public bool Admitted { get; init; }

    /// <summary>
    /// Gets the reason for exclusion, if any.
    /// </summary>
    public string? ExclusionReason { get; init; }
}
