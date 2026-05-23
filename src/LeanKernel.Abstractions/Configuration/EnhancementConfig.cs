namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configures synchronous response enhancement behavior.
/// </summary>
public sealed class EnhancementConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether response enhancement is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether knowledge synthesis is enabled.
    /// </summary>
    public bool KnowledgeSynthesisEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether refusal interception is enabled.
    /// </summary>
    public bool RefusalInterceptionEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether inline citation injection is enabled.
    /// </summary>
    public bool CitationInjectionEnabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum time budget for the full enhancement pipeline in milliseconds.
    /// </summary>
    public int MaxEnhancementTimeMs { get; set; } = 5000;
}
