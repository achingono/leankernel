using System.ComponentModel.DataAnnotations;

namespace LeanKernel.Entities;

/// <summary>
/// Persists structured model/provider/token-usage/cost telemetry for a single assistant turn.
/// One-to-one with <see cref="TurnEntity"/> keyed by <see cref="TurnId"/>.
/// </summary>
public sealed class TurnTelemetryEntity : IAuditable, IRecyclable
{
    /// <summary>
    /// Gets or sets the unique telemetry record identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the turn identifier this telemetry is associated with.
    /// </summary>
    public required Guid TurnId { get; set; }

    /// <summary>
    /// Gets or sets the model alias or name that was requested from the gateway.
    /// </summary>
    public string? RequestedModel { get; set; }

    /// <summary>
    /// Gets or sets the model name actually served by the provider.
    /// </summary>
    public string? ServedModel { get; set; }

    /// <summary>
    /// Gets or sets the normalized provider name.
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
    /// Gets or sets the currency code for the response cost.
    /// </summary>
    [MaxLength(10)]
    public string? Currency { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cost was estimated from token counts.
    /// </summary>
    public bool CostIsEstimated { get; set; }

    /// <summary>
    /// Gets or sets the round-trip latency of the model response in milliseconds.
    /// </summary>
    public long? LatencyMs { get; set; }

    /// <summary>
    /// Gets or sets when this telemetry was captured.
    /// </summary>
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the telemetry schema version for forward-compatible deserialization.
    /// </summary>
    [MaxLength(10)]
    public string SchemaVersion { get; set; } = "1.0";

    /// <summary>
    /// Gets or sets the parent turn entity.
    /// </summary>
    public TurnEntity Turn { get; set; } = null!;

    /// <summary>
    /// Date and time when the telemetry record was created.
    /// </summary>
    [Required]
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// Badge of the user who created the telemetry record.
    /// </summary>
    [Required]
    public Badge CreatedBy { get; set; } = default!;

    /// <summary>
    /// Date and time when the telemetry record was last updated.
    /// </summary>
    public DateTime? UpdatedOn { get; set; }

    /// <summary>
    /// Badge of the user who last updated the telemetry record.
    /// </summary>
    public Badge? UpdatedBy { get; set; }

    /// <summary>
    /// Indicates whether the telemetry record is soft-deleted.
    /// </summary>
    public bool IsDeleted { get; set; }
}
