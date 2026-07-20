namespace LeanKernel.Services.Common;

/// <summary>
/// Configuration options for publishing completed turns to the learning service.
/// </summary>
public sealed class LearningServiceOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether publishing is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the base URL of the learning service.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the relative ingest route used for completed turns.
    /// </summary>
    public string IngestPath { get; set; } = LearningServiceRoutes.TurnEventsPath;

    /// <summary>
    /// Gets or sets the outbound request timeout, in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 2;
}
