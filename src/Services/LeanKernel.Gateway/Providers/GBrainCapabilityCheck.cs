using Microsoft.Extensions.Logging;

namespace LeanKernel.Gateway.Providers;

/// <summary>
/// Possible outcomes of the GBrain capability pre-check.
/// </summary>
public enum GBrainCapabilityStatus
{
    /// <summary>All required operations are available.</summary>
    Full,
    /// <summary>Only a subset of operations is available (e.g. get_page or put_page missing).</summary>
    Degraded,
    /// <summary>GBrain is unreachable or auth is invalid.</summary>
    Unavailable,
    /// <summary>Local configuration is missing required transport settings.</summary>
    Misconfigured
}

/// <summary>
/// Describes the result of the GBrain capability pre-check.
/// </summary>
public sealed class GBrainCapabilityResult
{
    /// <summary>The probe outcome.</summary>
    public GBrainCapabilityStatus Status { get; init; }

    /// <summary>Whether <c>wiki_search</c> should be registered.</summary>
    public bool CanSearch { get; init; }

    /// <summary>Whether <c>wiki_read</c> should be registered.</summary>
    public bool CanRead { get; init; }

    /// <summary>Whether <c>wiki_write</c> should be registered.</summary>
    public bool CanWrite { get; init; }

    /// <summary>Human-readable diagnostic message.</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Performs the GBrain capability pre-check at startup per Appendix D.
/// Probes for search, get_page, and put_page support and returns a structured result.
/// </summary>
public sealed class GBrainCapabilityCheck
{
    private readonly IGBrainMcpClient _client;
    private readonly ILogger<GBrainCapabilityCheck> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="GBrainCapabilityCheck"/>.
    /// </summary>
    public GBrainCapabilityCheck(IGBrainMcpClient client, ILogger<GBrainCapabilityCheck> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs the capability probe and returns the result.
    /// </summary>
    public async Task<GBrainCapabilityResult> ProbeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("GBrain capability pre-check started.");

        bool canSearch = false, canRead = false, canWrite = false;

        canSearch = await ProbeOperationAsync("search", new { query = "__lk_probe__", limit = 1 }, ct)
            .ConfigureAwait(false);

        if (!canSearch)
        {
            _logger.LogWarning("GBrain search probe failed — wiki tools will not be registered.");
            return new GBrainCapabilityResult
            {
                Status = GBrainCapabilityStatus.Unavailable,
                Reason = "GBrain search operation not available."
            };
        }

        canRead = await ProbeOperationAsync("get_page", new { slug = "__lk_probe__" }, ct)
            .ConfigureAwait(false);

        canWrite = await ProbeOperationAsync("put_page", new { slug = "__lk_probe_write__", content = "probe" }, ct)
            .ConfigureAwait(false);

        var status = (canRead && canWrite) ? GBrainCapabilityStatus.Full : GBrainCapabilityStatus.Degraded;

        var reason = status == GBrainCapabilityStatus.Full
            ? "All GBrain knowledge operations are available."
            : $"GBrain degraded: read={canRead}, write={canWrite}. Some wiki tools may not be registered.";

        _logger.LogInformation("GBrain capability pre-check complete: {Status} — {Reason}", status, reason);

        return new GBrainCapabilityResult
        {
            Status = status,
            CanSearch = canSearch,
            CanRead = canRead,
            CanWrite = canWrite,
            Reason = reason
        };
    }

    private async Task<bool> ProbeOperationAsync(string toolName, object args, CancellationToken ct)
    {
        try
        {
            await _client.CallToolAsync(toolName, args, ct).ConfigureAwait(false);
            return true;
        }
        catch (GBrainException ex) when (ex.ErrorCode == -32601)
        {
            // Method not found — tool not supported
            _logger.LogDebug("GBrain tool '{Tool}' not found (code={Code}).", toolName, ex.ErrorCode);
            return false;
        }
        catch (GBrainException ex)
        {
            // Other GBrain errors (e.g., page not found for probe key) count as supported
            _logger.LogDebug("GBrain tool '{Tool}' probe returned error code {Code}: {Message}. Treating as supported.", toolName, ex.ErrorCode, ex.Message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GBrain tool '{Tool}' probe failed with unexpected error.", toolName);
            return false;
        }
    }
}
