namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// Records the gatekeeper's admission decision for a context item.
/// </summary>
public sealed class AdmissionRecord
{
    /// <summary>
    /// The source of the context item.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Whether the item was admitted.
    /// </summary>
    public required bool Admitted { get; init; }

    /// <summary>
    /// The reason for the decision (e.g., "budget_exhausted", "low_score", "admitted").
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Token cost of this item.
    /// </summary>
    public int TokenCost { get; init; }

    /// <summary>
    /// Remaining budget after this decision.
    /// </summary>
    public int RemainingBudget { get; init; }
}