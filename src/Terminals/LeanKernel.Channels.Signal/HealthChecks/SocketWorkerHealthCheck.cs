using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels.Signal.HealthChecks;

/// <summary>
/// Health check that monitors per-account signal socket worker progress and manager liveness.
/// </summary>
/// <param name="provider">The socket worker health provider.</param>
/// <param name="options">The signal channel settings.</param>
/// <param name="logger">The logger.</param>
public sealed class SocketWorkerHealthCheck(
    ISocketWorkerHealthProvider provider,
    IOptions<SignalSettings> options,
    ILogger<SocketWorkerHealthCheck> logger) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = options.Value;
        var now = DateTime.UtcNow;

        if (!settings.EnableWorkerHealthCheck)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Worker health check disabled."));
        }

        var (started, running) = provider.GetManagerState();
        if (started && !running)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Socket worker manager task is not running or crashed."));
        }

        if (!provider.IsInitialDiscoveryCompleted)
        {
            var startupGrace = TimeSpan.FromSeconds(Math.Max(30, settings.AccountRefreshSeconds * 3));
            if (started && now - provider.StartedUtc > startupGrace)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    "Initial account discovery is taking longer than expected."));
            }

            return Task.FromResult(HealthCheckResult.Healthy("Initial account discovery in progress."));
        }

        var workerStates = provider.GetWorkerStates();
        if (workerStates.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy("No configured accounts to monitor."));
        }

        var anyUnhealthy = false;
        var anyDegraded = false;
        var unhealthyAccounts = new List<string>();
        var degradedAccounts = new List<string>();
        var data = new Dictionary<string, object>
        {
            ["transportStarted"] = started,
            ["managerRunning"] = running,
            ["initialDiscoveryCompleted"] = provider.IsInitialDiscoveryCompleted
        };

        foreach (var worker in workerStates.Values)
        {
            data[worker.Account] = worker;

            var status = Evaluate(worker, now, settings);
            if (status == HealthStatus.Unhealthy)
            {
                anyUnhealthy = true;
                unhealthyAccounts.Add(worker.Account);
            }
            else if (status == HealthStatus.Degraded)
            {
                anyDegraded = true;
                degradedAccounts.Add(worker.Account);
            }
        }

        logger.LogDebug(
            "Socket worker health evaluated as Unhealthy={AnyUnhealthy} Degraded={AnyDegraded} for {AccountCount} account(s).",
            anyUnhealthy,
            anyDegraded,
            workerStates.Count);

        if (anyUnhealthy)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Socket worker(s) unhealthy: {string.Join(", ", unhealthyAccounts)}.",
                data: data));
        }

        if (anyDegraded)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Socket worker(s) degraded: {string.Join(", ", degradedAccounts)}.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Socket worker health check passed.", data: data));
    }

    private static HealthStatus Evaluate(SocketWorkerHealthState worker, DateTime now, SignalSettings settings)
    {
        var degradedThreshold = TimeSpan.FromSeconds(Math.Max(0, settings.WorkerDegradedThresholdSeconds));
        var unhealthyThreshold = TimeSpan.FromSeconds(Math.Max(0, settings.WorkerUnhealthyThresholdSeconds));

        var lastReceive = worker.LastSuccessfulReceiveUtc ?? DateTime.MinValue;
        var lastLoopTick = worker.LastWorkerLoopTickUtc ?? DateTime.MinValue;
        var hasProgress = worker.LastSuccessfulReceiveUtc.HasValue || worker.LastWorkerLoopTickUtc.HasValue;
        var progressAge = hasProgress
            ? now - (lastReceive > lastLoopTick ? lastReceive : lastLoopTick)
            : TimeSpan.Zero;

        if (worker.State == SocketWorkerState.Faulted
            || worker.ConsecutiveErrors >= settings.WorkerUnhealthyErrorThreshold
            || (hasProgress && progressAge > unhealthyThreshold)
            || (worker.State == SocketWorkerState.Starting && now - worker.StartedUtc > unhealthyThreshold))
        {
            return HealthStatus.Unhealthy;
        }

        if ((worker.State == SocketWorkerState.Starting && now - worker.StartedUtc > degradedThreshold)
            || (worker.State == SocketWorkerState.Running
                && (progressAge > degradedThreshold || worker.ConsecutiveErrors >= settings.WorkerConsecutiveErrorThreshold)))
        {
            return HealthStatus.Degraded;
        }

        return HealthStatus.Healthy;
    }
}
