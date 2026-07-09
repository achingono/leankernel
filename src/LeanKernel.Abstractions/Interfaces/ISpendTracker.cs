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
    /// Attempts to reserve spend atomically against configured limits.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="turnId">The turn identifier.</param>
    /// <param name="amountUsd">The amount to reserve in USD.</param>
    /// <param name="maxDailySpendUsd">The maximum daily spend limit.</param>
    /// <param name="maxSessionSpendUsd">The maximum session spend limit.</param>
    /// <param name="maxMonthlySpendUsd">The maximum monthly spend limit.</param>
    /// <param name="reservation">The created reservation when successful.</param>
    /// <param name="asOf">The optional evaluation time.</param>
    /// <returns><see langword="true"/> when reservation succeeds; otherwise <see langword="false"/>.</returns>
    bool TryReserveSpend(
        string sessionId,
        string turnId,
        decimal amountUsd,
        decimal maxDailySpendUsd,
        decimal maxSessionSpendUsd,
        decimal maxMonthlySpendUsd,
        out SpendReservation? reservation,
        DateTimeOffset? asOf = null);

    /// <summary>
    /// Commits an existing reservation as actual spend.
    /// </summary>
    /// <param name="reservation">The reservation to commit.</param>
    /// <param name="actualAmountUsd">Optional actual amount. When omitted, the reserved amount is committed.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated spend snapshot.</returns>
    Task<SpendSnapshot> CommitReservedSpendAsync(SpendReservation reservation, decimal? actualAmountUsd = null, CancellationToken ct = default);

    /// <summary>
    /// Releases an existing reservation without recording spend.
    /// </summary>
    /// <param name="reservation">The reservation to release.</param>
    void ReleaseReservedSpend(SpendReservation reservation);

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
