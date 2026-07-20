namespace LeanKernel.Logic.Tools.Dynamic;

/// <summary>
/// Authentication configuration for a skill.
/// </summary>
public sealed class SkillAuthConfig
{
    /// <summary>
    /// Gets the auth type: "none" or "bearer".
    /// </summary>
    public string Type { get; init; } = "none";

    /// <summary>
    /// Gets the secret reference name resolved from /run/secrets/&lt;ref&gt; or SKILL__&lt;REF&gt;.
    /// </summary>
    public string? SecretRef { get; init; }
}
