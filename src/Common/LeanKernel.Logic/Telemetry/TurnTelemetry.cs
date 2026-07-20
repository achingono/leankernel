namespace LeanKernel.Logic.Telemetry;

/// <summary>
/// Structured model/provider/token-usage/cost telemetry captured for each assistant turn.
/// </summary>
public sealed class TurnTelemetry
{
    /// <summary>
    /// Gets or sets the schema version for forward-compatible deserialization.
    /// </summary>
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>
    /// Gets or sets the model alias or name that was requested from the gateway (e.g. "tool", "gpt-4o-mini").
    /// </summary>
    public string? RequestedModel { get; set; }

    /// <summary>
    /// Gets or sets the model name actually served by the provider (e.g. "gpt-4o", "claude-3-opus").
    /// </summary>
    public string? ServedModel { get; set; }

    /// <summary>
    /// Gets or sets the normalized provider name (e.g. "azure", "openai", "groq").
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Gets or sets the provider-specific model identifier.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Gets or sets the API base URL used for the request.
    /// </summary>
    public string? ApiBase { get; set; }

    /// <summary>
    /// Gets or sets the number of prompt/input tokens consumed.
    /// </summary>
    public int? PromptTokens { get; set; }

    /// <summary>
    /// Gets or sets the number of completion/output tokens generated.
    /// </summary>
    public int? CompletionTokens { get; set; }

    /// <summary>
    /// Gets or sets the total token count (prompt + completion).
    /// </summary>
    public int? TotalTokens { get; set; }

    /// <summary>
    /// Gets or sets the response cost reported by the provider or estimated from token counts.
    /// </summary>
    public decimal? ResponseCost { get; set; }

    /// <summary>
    /// Gets or sets the currency code for the response cost (e.g. "USD").
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cost was estimated from token counts
    /// rather than reported by the provider.
    /// </summary>
    public bool CostIsEstimated { get; set; }

    /// <summary>
    /// Gets or sets the round-trip latency of the model response.
    /// </summary>
    public TimeSpan? Latency { get; set; }

    /// <summary>
    /// Gets or sets when this telemetry was captured.
    /// </summary>
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}