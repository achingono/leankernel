namespace LeanKernel.Logic.Telemetry;

/// <summary>
/// PII-free telemetry export record for model-learning workflows.
/// </summary>
public sealed record TelemetryExportRecord(
    DateTimeOffset Timestamp,
    string RequestedModel,
    string ServedModel,
    string Provider,
    int PromptTokens,
    int CompletionTokens,
    decimal? ResponseCost,
    bool CostIsEstimated);
