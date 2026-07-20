namespace LeanKernel.Logic.Telemetry.Models;

/// <summary>
/// Headline telemetry summary for a date range.
/// </summary>
public sealed record CostSummary(
    DateRange Range,
    decimal TotalCost,
    int TotalPromptTokens,
    int TotalCompletionTokens,
    int TotalTurns,
    int UniqueUsers,
    int UniqueSessions,
    int UniqueModels,
    decimal AvgCostPerTurn,
    decimal AvgTokensPerTurn,
    string Currency);