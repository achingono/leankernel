namespace LeanKernel.Logic.Telemetry;

/// <summary>
/// Per-model token cost rates used to estimate response cost when the provider does not
/// report it directly. Keys are model name or alias; values are cost per 1,000 tokens.
/// </summary>
public sealed class CostEstimateTable
{
    /// <summary>
    /// Gets or sets the cost per 1,000 input/prompt tokens, keyed by model name.
    /// </summary>
    public Dictionary<string, decimal> CostPer1kInputTokens { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the cost per 1,000 output/completion tokens, keyed by model name.
    /// </summary>
    public Dictionary<string, decimal> CostPer1kOutputTokens { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Estimates the cost for the given token counts using the configured per-model rates.
    /// Returns null if no rate is configured for the model.
    /// </summary>
    /// <param name="model">The model name to look up.</param>
    /// <param name="promptTokens">The number of input tokens.</param>
    /// <param name="completionTokens">The number of output tokens.</param>
    /// <returns>The estimated cost, or null if the model has no configured rate.</returns>
    public decimal? Estimate(string? model, int promptTokens, int completionTokens)
    {
        if (string.IsNullOrEmpty(model))
            return null;

        CostPer1kInputTokens.TryGetValue(model, out var inputRate);
        CostPer1kOutputTokens.TryGetValue(model, out var outputRate);

        if (inputRate == 0 && outputRate == 0)
            return null;

        var inputCost = promptTokens / 1_000m * inputRate;
        var outputCost = completionTokens / 1_000m * outputRate;
        return inputCost + outputCost;
    }
}
