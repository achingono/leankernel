using LeanKernel.Logic.Memory;
using LeanKernel.Services.Common.Interfaces;

using Microsoft.Extensions.Logging;

namespace LeanKernel.Services.Common.Memory;

/// <summary>
/// Performs the GBrain capability pre-check at startup per Appendix D.
/// Probes for search, get_page, and put_page support and returns a structured result.
/// </summary>
public sealed class GBrainCapabilityCheck : IMemoryCapabilityCheck
{
    private readonly IGBrainMcpClient _client;
    private readonly ILogger<GBrainCapabilityCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GBrainCapabilityCheck"/> class.
    /// Initializes a new instance of <see cref="GBrainCapabilityCheck"/>.
    /// </summary>
    /// <param name="client">The GBrain MCP client.</param>
    /// <param name="logger">The logger instance.</param>
    public GBrainCapabilityCheck(IGBrainMcpClient client, ILogger<GBrainCapabilityCheck> logger)
    {
        this._client = client ?? throw new ArgumentNullException(nameof(client));
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs the capability probe and returns the result.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The <see cref="MemoryCapabilityResult"/> from the probe.</returns>
    public async Task<MemoryCapabilityResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        this._logger.LogInformation("GBrain capability pre-check started.");

        bool canSearch = false, canRead = false, canWrite = false;

        canSearch = await this.ProbeOperationAsync("search", new { query = "__lk_probe__", limit = 1 }, cancellationToken)
            .ConfigureAwait(false);

        if (!canSearch)
        {
            this._logger.LogWarning("GBrain search probe failed — wiki tools will not be registered.");
            return new MemoryCapabilityResult
            {
                Status = MemoryCapabilityStatus.Unavailable,
                Reason = "GBrain search operation not available.",
            };
        }

        canRead = await this.ProbeOperationAsync("get_page", new { slug = "__lk_probe__" }, cancellationToken)
            .ConfigureAwait(false);

        canWrite = await this.ProbeOperationAsync("put_page", new { slug = "__lk_probe_write__", content = "probe" }, cancellationToken)
            .ConfigureAwait(false);

        var status = (canRead && canWrite) ? MemoryCapabilityStatus.Full : MemoryCapabilityStatus.Degraded;

        var reason = status == MemoryCapabilityStatus.Full
            ? "All GBrain knowledge operations are available."
            : $"GBrain degraded: read={canRead}, write={canWrite}. Some wiki tools may not be registered.";

        this._logger.LogInformation("GBrain capability pre-check complete: {Status} — {Reason}", status, reason);

        return new MemoryCapabilityResult
        {
            Status = status,
            CanSearch = canSearch,
            CanRead = canRead,
            CanWrite = canWrite,
            Reason = reason,
        };
    }

    private async Task<bool> ProbeOperationAsync(string toolName, object args, CancellationToken ct)
    {
        try
        {
            await this._client.CallToolAsync(toolName, args, ct).ConfigureAwait(false);
            return true;
        }
        catch (GBrainException ex) when (ex.ErrorCode == -32601)
        {
            // Method not found — tool not supported
            this._logger.LogDebug("GBrain tool '{Tool}' not found (code={Code}).", toolName, ex.ErrorCode);
            return false;
        }
        catch (GBrainException ex)
        {
            // Other GBrain errors (e.g., page not found for probe key) count as supported
            this._logger.LogDebug("GBrain tool '{Tool}' probe returned error code {Code}: {Message}. Treating as supported.", toolName, ex.ErrorCode, ex.Message);
            return true;
        }
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "GBrain tool '{Tool}' probe failed with unexpected error.", toolName);
            return false;
        }
    }
}