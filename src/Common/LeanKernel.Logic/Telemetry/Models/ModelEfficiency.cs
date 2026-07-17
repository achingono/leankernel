namespace LeanKernel.Logic.Telemetry.Models;

/// <summary>
/// Model-level efficiency metrics derived from telemetry.
/// </summary>
public sealed record ModelEfficiency(
    string Model,
    string Provider,
    int TotalTurns,
    int TotalTokens,
    decimal TotalCost,
    decimal CostPer1kTokens,
    decimal AvgPromptTokensPerTurn,
    decimal AvgCompletionTokensPerTurn,
    decimal CompletionRatio);
