using LeanKernel.Abstractions.Enums;

namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents a spend-guard decision for one request.
/// </summary>
public sealed record SpendGuardDecision
{
    /// <summary>
    /// Gets the spend-guard action.
    /// </summary>
    public SpendGuardAction Action { get; init; } = SpendGuardAction.Allow;

    /// <summary>
    /// Gets the decision reason.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Gets the estimated request cost in USD.
    /// </summary>
    public decimal EstimatedCostUsd { get; init; }

    /// <summary>
    /// Gets the projected daily spend in USD.
    /// </summary>
    public decimal DailySpendUsd { get; init; }

    /// <summary>
    /// Gets the projected session spend in USD.
    /// </summary>
    public decimal SessionSpendUsd { get; init; }

    /// <summary>
    /// Gets the projected monthly spend in USD.
    /// </summary>
    public decimal MonthlySpendUsd { get; init; }

    /// <summary>
    /// Gets the daily spend limit in USD.
    /// </summary>
    public decimal DailyLimitUsd { get; init; }

    /// <summary>
    /// Gets the session spend limit in USD.
    /// </summary>
    public decimal SessionLimitUsd { get; init; }

    /// <summary>
    /// Gets the monthly spend limit in USD.
    /// </summary>
    public decimal MonthlyLimitUsd { get; init; }

    /// <summary>
    /// Gets the warning threshold percentage.
    /// </summary>
    public decimal WarningThresholdPercent { get; init; }
}
