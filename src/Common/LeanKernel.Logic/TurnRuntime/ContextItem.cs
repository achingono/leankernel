using Microsoft.Extensions.AI;

namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// A single candidate context item considered for admission into the prompt.
/// </summary>
public sealed class ContextItem
{
    /// <summary>
    /// Source category: "identity", "memory", "retrieval", "history", "system".
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// The text content of this context item.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Estimated token count for budget accounting.
    /// </summary>
    public int EstimatedTokens { get; init; }

    /// <summary>
    /// Relevance score (0.0 - 1.0). Higher = more relevant.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Optional metadata for diagnostics and filtering.
    /// </summary>
    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
