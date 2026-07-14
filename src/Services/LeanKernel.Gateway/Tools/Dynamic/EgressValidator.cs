using System.Net;

namespace LeanKernel.Gateway.Tools.Dynamic;

/// <summary>
/// Validates HTTP egress targets for dynamic skill tools,
/// blocking loopback, private, and link-local hosts.
/// </summary>
public static class EgressValidator
{
    /// <summary>
    /// Returns true when <paramref name="host"/> is a loopback, private, or link-local address.
    /// </summary>
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
    public static bool IsHostAllowed(
        string host,
        IReadOnlyList<string> skillAllowedHosts,
        IReadOnlyList<string> globalAllowHosts)
    {
        if (IsPrivateOrLoopbackHost(host))
        {
            return false;
        }

        var inSkill = skillAllowedHosts.Any(h =>
            string.Equals(h, host, StringComparison.OrdinalIgnoreCase));

        if (!inSkill)
        {
            return false;
        }

        if (globalAllowHosts.Count == 0)
        {
            return true;
        }

        return globalAllowHosts.Any(h =>
            string.Equals(h, host, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Attempts to validate an absolute URI target against the egress policy.
    /// Returns null when valid, or an error message when rejected.
    /// </summary>
    public static string? TryValidateEgressTarget(
        string url,
        IReadOnlyList<string> skillAllowedHosts,
        IReadOnlyList<string> globalAllowHosts)
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
        if (!IsHostAllowed(host, skillAllowedHosts, globalAllowHosts))
        {
            return $"Host '{host}' is not in the egress allowlist or is a private/loopback address.";
        }

        return null;
    }

    private static bool IsPrivateRange(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();

        if (bytes.Length == 4)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10)
            {
                return true;
            }

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // 127.0.0.0/8 (loopback range)
            if (bytes[0] == 127)
            {
                return true;
            }

            // 169.254.0.0/16 (link-local)
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }
        }

        if (bytes.Length == 16)
        {
            // IPv6 link-local: fe80::/10
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
            {
                return true;
            }

            // IPv6 unique-local: fc00::/7
            if ((bytes[0] & 0xfe) == 0xfc)
            {
                return true;
            }
        }

        return false;
    }
}
