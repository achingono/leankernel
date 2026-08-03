namespace LeanKernel.Logic.Tools.Dynamic;

/// <summary>
/// Resolves dynamic skill bearer secrets from <c>/run/secrets/&lt;ref&gt;</c> or
/// the <c>SKILL__&lt;REF&gt;</c> environment variable.
/// </summary>
internal static class SkillSecretResolver
{
    /// <summary>
    /// Resolves a bearer token for the given secret reference.
    /// </summary>
    /// <param name="secretRef">The secret reference name.</param>
    /// <param name="error">The resolution error message, or null on success.</param>
    /// <returns>The resolved secret value, or null when not found.</returns>
    public static string? Resolve(string? secretRef, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(secretRef))
        {
            error = "auth.secretRef is required for bearer authentication but is not set.";
            return null;
        }

        // Try /run/secrets/<ref> first
        var filePath = $"/run/secrets/{secretRef}";
        if (File.Exists(filePath))
        {
            return File.ReadAllText(filePath).Trim();
        }

        // Try SKILL__<REF_UPPER> env var
        var envVar = $"SKILL__{secretRef.ToUpperInvariant().Replace('-', '_')}";
        var envVal = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(envVal))
        {
            return envVal;
        }

        error = $"Secret '{secretRef}' not found in /run/secrets/{secretRef} or environment variable {envVar}.";
        return null;
    }

    /// <summary>
    /// Returns the environment variable name used to surface a resolved secret to a CLI child
    /// process: <c>SKILL__&lt;REF&gt;</c>.
    /// </summary>
    /// <param name="secretRef">The secret reference name.</param>
    /// <returns>The child-process environment variable name.</returns>
    public static string ToChildEnvironmentKey(string secretRef) =>
        $"SKILL__{secretRef.ToUpperInvariant().Replace('-', '_')}";
}
