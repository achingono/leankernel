namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configures model telemetry capture, cost estimation, and retention.
/// Bound from <c>Agents:Telemetry</c>.
/// </summary>
public sealed class TelemetrySettings
{
    /// <summary>
    /// Gets or sets a value indicating whether telemetry capture is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the currency code for cost reporting (e.g. "USD").
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Gets or sets a value indicating whether raw LiteLLM metadata is retained alongside structured telemetry.
    /// </summary>
    public bool RetainRawMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether token-based cost estimates are used when the provider does not report cost.
    /// </summary>
    public bool UseCostEstimate { get; set; } = true;
}