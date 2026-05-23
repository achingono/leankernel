using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Tracks accumulated spend for the current node.
/// </summary>
public interface ISpendTracker
{
    /// <summary>
    /// Gets the current spend snapshot.
    /// </summary>
    /// <param name="asOf">The optional time to evaluate rollover boundaries against.</param>
    /// <returns>The spend snapshot.</returns>
    SpendSnapshot GetSnapshot(DateTimeOffset? asOf = null);

    /// <summary>
    /// Records spend for a session and turn.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="turnId">The turn identifier.</param>
    /// <param name="amountUsd">The spend amount in USD.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated spend snapshot.</returns>
    Task<SpendSnapshot> RecordSpendAsync(string sessionId, string turnId, decimal amountUsd, CancellationToken ct = default);
}
