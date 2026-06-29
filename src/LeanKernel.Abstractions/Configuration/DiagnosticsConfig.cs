namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configuration settings for diagnostics.
/// </summary>
public sealed class DiagnosticsConfig
{
    /// <summary>
    /// Gets or sets whether diagnostics are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether diagnostics should be persisted to the database.
    /// </summary>
    public bool PersistToDatabase { get; set; } = true;

    /// <summary>
    /// Gets or sets whether context diagnostics are enabled.
    /// </summary>
    public bool ContextDiagnosticsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of diagnostics per session.
    /// </summary>
    public int MaxDiagnosticsPerSession { get; set; } = 100;

    /// <summary>
    /// Gets or sets the name of the service.
    /// </summary>
    public string ServiceName { get; set; } = "leankernel";
}
