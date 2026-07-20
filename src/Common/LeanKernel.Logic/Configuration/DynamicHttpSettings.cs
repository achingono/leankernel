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
}
