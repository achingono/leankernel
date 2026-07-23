namespace LeanKernel.Events;

/// <summary>
/// Represents model/provider telemetry captured for a single assistant turn.
/// Aligned with the DTO from <c>LeanKernel.Logic.Telemetry</c> for migration coexistence.
/// </summary>
public sealed record TelemetryEvent : IHasEnvelope
{
    /// <summary>
    /// Gets the event envelope providing partitioning and correlation metadata.
    /// </summary>
    public required EventEnvelope Envelope { get; init; }

    /// <summary>
    /// Gets the model alias or name that was requested.
    /// </summary>
    public string? RequestedModel { get; init; }

    /// <summary>
    /// Gets the model name actually served by the provider.
    /// </summary>
    public string? ServedModel { get; init; }

    /// <summary>
    /// Gets the normalized provider name (e.g. "azure", "openai").
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Gets the provider-specific model identifier.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Gets the API base URL used for the request.
    /// </summary>
    public string? ApiBase { get; init; }

    /// <summary>
    /// Gets the number of prompt/input tokens consumed.
    /// </summary>
    public int? PromptTokens { get; init; }

    /// <summary>
    /// Gets the number of completion/output tokens generated.
    /// </summary>
    public int? CompletionTokens { get; init; }

    /// <summary>
    /// Gets the total token count (prompt + completion).
    /// </summary>
    public int? TotalTokens { get; init; }

    /// <summary>
    /// Gets the response cost reported or estimated.
    /// </summary>
    public decimal? ResponseCost { get; init; }

    /// <summary>
    /// Gets the currency code for the response cost.
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>
    /// Gets a value indicating whether the cost was estimated.
    /// </summary>
    public bool CostIsEstimated { get; init; }

    /// <summary>
    /// Gets the round-trip latency in milliseconds.
    /// </summary>
    public long? LatencyMs { get; init; }

    /// <summary>
    /// Gets when this telemetry was captured.
    /// </summary>
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
}