namespace LeanKernel.Plugins.BuiltIn.Skills;

/// <summary>
/// Egress policy that validates HTTP requests against an allowlist of hosts.
/// </summary>
public interface IEgressPolicy
{
    /// <summary>
    /// Check if a URL is allowed by the policy.
    /// </summary>
    bool IsUrlAllowed(string url);

    /// <summary>
    /// Check if a host is in the allowlist.
    /// </summary>
    bool IsHostAllowed(string host);
}

/// <summary>
/// HTTP message handler that enforces egress policies on outgoing requests.
/// </summary>
public sealed class EgressPolicyHandler : HttpClientHandler
{
    private readonly IEgressPolicy _policy;

    /// <summary>
    /// Initializes a new instance of the <see cref="EgressPolicyHandler" /> class.
    /// </summary>
    /// <param name="policy">The policy.</param>
    /// <returns>The operation result.</returns>
    public EgressPolicyHandler(IEgressPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_policy.IsUrlAllowed(request.RequestUri?.ToString() ?? ""))
            throw new InvalidOperationException($"Egress denied: {request.RequestUri} is not in the allowlist");

        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Skill-specific egress policy based on runtime.egress.allowHosts.
/// </summary>
public sealed class SkillEgressPolicy : IEgressPolicy
{
    private readonly List<string> _allowHosts;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkillEgressPolicy" /> class.
    /// </summary>
    /// <param name="allowHosts">The allow hosts.</param>
    /// <returns>The operation result.</returns>
    public SkillEgressPolicy(List<string> allowHosts)
    {
        _allowHosts = allowHosts ?? [];
    }

    /// <summary>
    /// Executes the is url allowed operation.
    /// </summary>
    /// <param name="url">The url.</param>
    /// <returns>The operation result.</returns>
    public bool IsUrlAllowed(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // Extract host with port
        var host = uri.Host;
        var port = uri.Port;
        var hostWithPort = port != -1 ? $"{host}:{port}" : host;

        return IsHostAllowed(hostWithPort) || IsHostAllowed(host);
    }

    /// <summary>
    /// Executes the is host allowed operation.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <returns>The operation result.</returns>
    public bool IsHostAllowed(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        // Normalize host (remove trailing dot if present)
        host = host.TrimEnd('.');

        // Check for exact match or wildcard match
        foreach (var allowed in _allowHosts)
        {
            var normalizedAllowed = allowed.TrimEnd('.');

            // Exact match
            if (string.Equals(host, normalizedAllowed, StringComparison.OrdinalIgnoreCase))
                return true;

            // Wildcard match (e.g., "*.example.com" matches "subdomain.example.com")
            if (normalizedAllowed.StartsWith("*."))
            {
                var domain = normalizedAllowed[2..];
                if (host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
