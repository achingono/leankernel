using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Selects the most relevant LeanKernels that fit within a context token budget.
/// </summary>
public interface ILeanKernelSelectionStrategy
{
    /// <summary>
    /// Selects and ranks candidate LeanKernels for inclusion in context.
    /// </summary>
    /// <param name="candidates">The candidate LeanKernels to score and select.</param>
    /// <param name="tokenBudget">The maximum token budget available for selected candidates.</param>
    /// <param name="exclusionLog">The exclusion log to append budget and threshold decisions to.</param>
    /// <returns>The selected candidates sorted by descending relevance.</returns>
    IReadOnlyList<RelevanceScore> Select(
        IReadOnlyList<RelevanceScore> candidates,
        int tokenBudget,
        IList<string> exclusionLog);
}
