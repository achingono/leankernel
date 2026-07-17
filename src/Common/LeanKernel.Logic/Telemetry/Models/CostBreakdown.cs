namespace LeanKernel.Logic.Telemetry.Models;

/// <summary>
/// Cost and token roll-up for a telemetry dimension.
/// </summary>
public sealed record CostBreakdown(
    string Dimension,
    string Key,
    decimal TotalCost,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    int TurnCount,
    decimal AvgCostPerTurn,
    decimal AvgTokensPerTurn,
    int EstimatedTurnCount,
    int ReportedTurnCount);
