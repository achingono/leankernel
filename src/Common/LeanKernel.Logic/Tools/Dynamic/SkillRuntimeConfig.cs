namespace LeanKernel.Logic.Tools.Dynamic;

/// <summary>
/// Runtime configuration for an HTTP skill.
/// </summary>
public sealed class SkillRuntimeConfig
{
    /// <summary>
    /// Gets the runtime type. Only "http" is supported in Phase 01.
    /// </summary>
    public string Type { get; init; } = "http";

    /// <summary>
    /// Gets the base URL for HTTP operations.
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Gets the per-request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Gets the auth configuration.
    /// </summary>
    public SkillAuthConfig Auth { get; init; } = new();
}