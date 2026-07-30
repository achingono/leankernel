namespace LeanKernel.Logic.Diagnostics;

using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Aggregates health check results from the registered health check services.
/// </summary>
/// <param name="healthCheckService">The health check service used to evaluate system health.</param>
public sealed class HealthAggregator(HealthCheckService healthCheckService) : IHealthAggregator
{
    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        var report = await healthCheckService.CheckHealthAsync(cancellationToken);
        return report.Status == HealthStatus.Healthy;
    }

    /// <inheritdoc />
    public async Task<bool> IsLiteLlmHealthyAsync(CancellationToken cancellationToken = default)
    {
        var report = await healthCheckService.CheckHealthAsync(cancellationToken);
        if (report.Entries.TryGetValue("litellm", out var entry))
        {
            return entry.Status == HealthStatus.Healthy;
        }

        return false;
    }
}