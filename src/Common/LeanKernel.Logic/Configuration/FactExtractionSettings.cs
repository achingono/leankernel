namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configures the model used to extract durable facts from conversation turns.
/// </summary>
public sealed class FactExtractionSettings
{
    /// <summary>
    /// Gets or sets the model identifier used for fact extraction.
    /// </summary>
    public string ModelId { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Gets or sets the sampling temperature used during fact extraction.
    /// </summary>
    public double Temperature { get; set; } = 0.1;

    /// <summary>
    /// Gets or sets the maximum number of output tokens returned by the model.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 1024;
}
