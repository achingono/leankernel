using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LeanKernel.Services.Common.HealthChecks;

/// <summary>
/// Health check that monitors the learning background worker liveness.
/// </summary>
public sealed class LearningWorkerHealthCheck(WorkerHealthState state) : IHealthCheck
{
    private static readonly TimeSpan MaxHeartbeatAge = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
    {
        var age = state.LearningWorkerHeartbeatAge;
        if (age == TimeSpan.MaxValue)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "Learning worker not yet started"));
        }

        if (age > MaxHeartbeatAge)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Learning worker last heartbeat was {age.TotalSeconds:F0}s ago (threshold: {MaxHeartbeatAge.TotalSeconds:F0}s)"));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Learning worker heartbeat {age.TotalSeconds:F0}s ago"));
    }
}
