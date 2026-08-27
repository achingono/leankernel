using System.Net;

namespace LeanKernel.Logic.Tools.Dynamic;

/// <summary>
/// Validates HTTP egress targets for dynamic skill tools,
/// blocking loopback, private, and link-local hosts.
/// </summary>
public static class EgressValidator
{
    /// <summary>
    /// Returns true when <paramref name="host"/> is a loopback, private, or link-local address.
    /// </summary>
    /// <param name="host">The request host (without port).</param>
    /// <returns>True when the host is private or loopback; false otherwise.</returns>
    public static bool IsPrivateOrLoopbackHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        var lower = host.ToLowerInvariant();

        // Well-known loopback / metadata names
        if (lower is "localhost" or "127.0.0.1" or "::1" or "[::1]")
        {
            return true;
        }

        // AWS / GCP metadata
        if (lower.StartsWith("169.254.", StringComparison.Ordinal) ||
            lower.StartsWith("metadata.", StringComparison.Ordinal))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var ip))
        {
            return false;
        }

        return IPAddress.IsLoopback(ip) || IsPrivateRange(ip);
    }

    /// <summary>
    /// Returns true when the host is in the effective allowlist.
    /// </summary>
    /// <param name="host">The request host (without port).</param>
    /// <param name="skillAllowedHosts">Per-skill allowlist from <c>egress.allowHosts</c>.</param>
    /// <param name="globalAllowHosts">Global ceiling from <c>Agents:Tools:DynamicHttp:AllowHosts</c>.</param>
    /// <param name="globalAllowPrivateHosts">Explicit private-host exceptions from <c>Agents:Tools:DynamicHttp:AllowPrivateHosts</c>.</param>
    /// <returns>True when the host is allowed; false otherwise.</returns>
    public static bool IsHostAllowed(
        string host,
        IReadOnlyList<string> skillAllowedHosts,
        IReadOnlyList<string> globalAllowHosts,
        IReadOnlyList<string>? globalAllowPrivateHosts = null)
    {
        var inSkill = MatchesHostInAllowList(host, skillAllowedHosts);

        if (!inSkill)
        {
            return false;
        }

        var inGlobal = globalAllowHosts.Count == 0 || MatchesHostInAllowList(host, globalAllowHosts);
        if (!inGlobal)
        {
            return false;
        }

        if (!IsPrivateOrLoopbackHost(host))
        {
            return true;
        }

        return MatchesHostInAllowList(host, globalAllowPrivateHosts ?? []);
    }

    /// <summary>
    /// Attempts to validate an absolute URI target against the egress policy.
    /// Returns null when valid, or an error message when rejected.
    /// </summary>
    /// <param name="url">The absolute URI to validate.</param>
    /// <param name="skillAllowedHosts">Per-skill allowlist from <c>egress.allowHosts</c>.</param>
    /// <param name="globalAllowHosts">Global ceiling from <c>Agents:Tools:DynamicHttp:AllowHosts</c>.</param>
    /// <param name="globalAllowPrivateHosts">Explicit private-host exceptions from <c>Agents:Tools:DynamicHttp:AllowPrivateHosts</c>.</param>
    /// <returns>Null when valid, or an error message when rejected.</returns>
    public static string? TryValidateEgressTarget(
        string url,
        IReadOnlyList<string> skillAllowedHosts,
        IReadOnlyList<string> globalAllowHosts,
        IReadOnlyList<string>? globalAllowPrivateHosts = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return $"Invalid URL: {url}";
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return $"Unsupported scheme '{uri.Scheme}'. Only http and https are allowed.";
        }

        var host = uri.Host;
        if (!IsHostAllowed(host, skillAllowedHosts, globalAllowHosts, globalAllowPrivateHosts))
        {
            return $"Host '{host}' is not in the egress allowlist or is a private/loopback address.";
        }

        return null;
    }

    private static bool IsPrivateRange(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return bytes.Length == 4 ? IsPrivateIPv4(bytes) : IsPrivateIPv6(bytes);
    }

    private static bool IsPrivateIPv4(byte[] b) =>
        b[0] == 10 // 10.0.0.0/8
        || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) // 172.16.0.0/12
        || (b[0] == 192 && b[1] == 168) // 192.168.0.0/16
        || b[0] == 127 // 127.0.0.0/8 loopback
        || (b[0] == 169 && b[1] == 254);           // 169.254.0.0/16 link-local

    private static bool IsPrivateIPv6(byte[] b) =>
        b.Length == 16 && (
            (b[0] == 0xfe && (b[1] & 0xc0) == 0x80) // fe80::/10 link-local
            || (b[0] & 0xfe) == 0xfc);               // fc00::/7 unique-local

    private static bool MatchesHostInAllowList(string host, IReadOnlyList<string> allowHosts)
        => allowHosts.Any(entry =>
            string.Equals(NormalizeAllowHost(entry), host, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeAllowHost(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri)
            && (absoluteUri.Scheme is "http" or "https")
            && !string.IsNullOrWhiteSpace(absoluteUri.Host))
        {
            return absoluteUri.Host;
        }

        var firstColon = trimmed.IndexOf(':');
        if (firstColon > -1 && firstColon == trimmed.LastIndexOf(':'))
        {
            return trimmed[..firstColon];
        }

        return trimmed.Trim('[', ']');
    }
}
