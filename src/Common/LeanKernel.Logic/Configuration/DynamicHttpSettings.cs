namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Global HTTP egress settings for dynamic skill tools, nested under <c>Agents:Tools:DynamicHttp</c>.
/// </summary>
public sealed class DynamicHttpSettings
{
    /// <summary>
    /// Gets or sets the global host allowlist ceiling for dynamic HTTP tool egress.
    /// An empty list means per-skill <c>egress.allowHosts</c> is authoritative alone.
    /// </summary>
    public IReadOnlyList<string> AllowHosts { get; set; } = [];

    /// <summary>
    /// Gets or sets private/loopback hosts that are explicitly allowed for dynamic HTTP egress.
    /// Entries should be exact hostnames (optionally with a port suffix).
    /// This list is evaluated only after per-skill and global allowlist checks pass.
    /// </summary>
    public IReadOnlyList<string> AllowPrivateHosts { get; set; } = [];
}
