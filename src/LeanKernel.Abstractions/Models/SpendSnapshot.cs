namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Captures node-local spend state.
/// </summary>
public sealed record SpendSnapshot
{
    /// <summary>
    /// Gets the current UTC day represented by this snapshot.
    /// </summary>
    public required DateOnly DayUtc { get; init; }

    /// <summary>
    /// Gets the first day of the current UTC month represented by this snapshot.
    /// </summary>
    public required DateOnly MonthUtc { get; init; }

    /// <summary>
    /// Gets the daily spend total in USD.
    /// </summary>
    public decimal DailyTotalUsd { get; init; }

    /// <summary>
    /// Gets the monthly spend total in USD.
    /// </summary>
    public decimal MonthlyTotalUsd { get; init; }

    /// <summary>
    /// Gets per-session spend totals in USD.
    /// </summary>
    public IReadOnlyDictionary<string, decimal> SessionTotalsUsd { get; init; } = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the recorded spend for one session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>The session spend in USD.</returns>
    public decimal GetSessionSpendUsd(string sessionId)
        => SessionTotalsUsd.TryGetValue(sessionId, out var value) ? value : 0m;
}
