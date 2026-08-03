namespace LeanKernel.Logic.Tools.Dynamic;

/// <summary>
/// Runtime configuration for a skill (HTTP or CLI).
/// </summary>
public class SkillRuntimeConfig
{
    /// <summary>
    /// Gets the runtime type: "http" or "cli".
    /// </summary>
    public string Type { get; init; } = "http";

    /// <summary>
    /// Gets the base URL for HTTP operations.
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Gets the binary name resolved via PATH for CLI operations.
    /// </summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>
    /// Gets the per-request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Gets the auth configuration.
    /// </summary>
    public SkillAuthConfig Auth { get; init; } = new();
}
