using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Host;

/// <summary>
/// Custom health check that verifies LeanKernel subsystem connectivity.
/// </summary>
public sealed class LeanKernelHealthCheck : IHealthCheck
{
    private readonly Func<LeanKernelConfig> _getConfig;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeanKernelHealthCheck" /> class.
    /// </summary>
    /// <param name="config">The config.</param>
    /// <returns>The operation result.</returns>
    [ActivatorUtilitiesConstructor]
    public LeanKernelHealthCheck(IOptionsMonitor<LeanKernelConfig> config)
    {
        _getConfig = () => config.CurrentValue;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LeanKernelHealthCheck" /> class.
    /// </summary>
    /// <param name="config">The config.</param>
    /// <returns>The operation result.</returns>
    public LeanKernelHealthCheck(IOptions<LeanKernelConfig> config)
    {
        _getConfig = () => config.Value;
    }

    /// <summary>
    /// Represents the check health async.
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var config = _getConfig();
        var data = new Dictionary<string, object>
        {
            ["wiki_path"] = config.Wiki.BasePath,
            ["litellm_url"] = config.LiteLlm.BaseUrl,
            ["qdrant_host"] = config.Qdrant.Host,
            ["uptime"] = (DateTime.UtcNow - _startTime).ToString()
        };

        // Check wiki directory exists and is writable
        var wikiPath = config.Wiki.BasePath;
        if (!Directory.Exists(wikiPath))
        {
            return HealthCheckResult.Degraded("Wiki directory not found", data: data);
        }

        try
        {
            var testFile = Path.Combine(wikiPath, ".health-check");
            await File.WriteAllTextAsync(testFile, "ok", cancellationToken);
            File.Delete(testFile);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Wiki directory not writable: {ex.Message}", data: data);
        }

        return HealthCheckResult.Healthy("LeanKernel is operational", data);
    }

    private static readonly DateTime _startTime = DateTime.UtcNow;
}
