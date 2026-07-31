using System.Text.Json;

using LeanKernel.Services.Common.Interfaces;

using Microsoft.Extensions.Logging;

namespace LeanKernel.Services.Common.Memory;

/// <summary>
/// Default Dream service implementation backed by the GBrain MCP client.
/// </summary>
public sealed class GBrainDreamService : IDreamService
{
    private readonly IGBrainMcpClient _mcpClient;
    private readonly ILogger<GBrainDreamService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GBrainDreamService"/> class.
    /// </summary>
    /// <param name="mcpClient">The MCP client.</param>
    /// <param name="logger">The logger.</param>
    public GBrainDreamService(IGBrainMcpClient mcpClient, ILogger<GBrainDreamService> logger)
    {
        _mcpClient = mcpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DreamRunResult> RunDreamAsync(string sourceScope, string mode, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);

        JsonElement? payload = null;

        try
        {
            payload = await _mcpClient.CallToolAsync(
                "dream",
                new
                {
                    source_scope = sourceScope,
                    mode,
                },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dream MCP call failed for scope {Scope} mode {Mode}", sourceScope, mode);
            return new DreamRunResult(sourceScope, mode, Constants.JobStatus.Failed, 0, 0, null);
        }

        if (payload is null)
        {
            return new DreamRunResult(sourceScope, mode, Constants.JobStatus.Completed, 0, 0, null);
        }

        var result = payload.Value;
        var status = ReadString(result, "status") ?? Constants.JobStatus.Completed;
        var totalPages = ReadInt(result, "total_pages") ?? ReadInt(result, "totalPages") ?? 0;
        var failedPages = ReadInt(result, "failed_pages") ?? ReadInt(result, "failedPages") ?? 0;

        string? phaseStatusJson = null;
        if (TryReadObjectOrArray(result, "phase_status", out var phaseStatus)
            || TryReadObjectOrArray(result, "phaseStatus", out phaseStatus)
            || TryReadObjectOrArray(result, "phases", out phaseStatus))
        {
            phaseStatusJson = phaseStatus.GetRawText();
        }

        return new DreamRunResult(sourceScope, mode, status, totalPages, failedPages, phaseStatusJson);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static bool TryReadObjectOrArray(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value)
            && (value.ValueKind == JsonValueKind.Object || value.ValueKind == JsonValueKind.Array))
        {
            return true;
        }

        value = default;
        return false;
    }
}