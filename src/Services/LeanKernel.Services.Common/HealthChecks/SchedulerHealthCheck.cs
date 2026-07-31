using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LeanKernel.Services.Common.HealthChecks;

/// <summary>
/// Health check that monitors the scheduler hosted service liveness.
/// </summary>
public sealed class SchedulerHealthCheck(WorkerHealthState state) : IHealthCheck
{
    private static readonly TimeSpan MaxHeartbeatAge = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
    {
        var age = state.SchedulerHeartbeatAge;
        if (age == TimeSpan.MaxValue)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "Scheduler not yet started"));
        }

        if (age > MaxHeartbeatAge)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Scheduler last heartbeat was {age.TotalSeconds:F0}s ago (threshold: {MaxHeartbeatAge.TotalSeconds:F0}s)"));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Scheduler heartbeat {age.TotalSeconds:F0}s ago"));
    }
}
