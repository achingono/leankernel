using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Diagnostics.Health;

/// <summary>
/// Tracks health transitions for external providers.
/// </summary>
public sealed class ProviderHealthTracker(
    IEnumerable<IProviderHealthProbe> probes,
    IOptions<HardeningConfig> config,
    LeanKernelMetrics metrics,
    ILogger<ProviderHealthTracker> logger,
    TimeProvider? timeProvider = null) : BackgroundService, IProviderHealthTracker
{
    private readonly IReadOnlyDictionary<string, IProviderHealthProbe> _probes = (probes ?? throw new ArgumentNullException(nameof(probes)))
        .ToDictionary(probe => probe.ProviderName, StringComparer.OrdinalIgnoreCase);
    private readonly HardeningConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
    private readonly LeanKernelMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    private readonly ILogger<ProviderHealthTracker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object _sync = new();
    private readonly Dictionary<string, ProviderHealthStatus> _providerStatuses = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ProviderHealthSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return CreateSnapshotLocked();
        }
    }

    /// <inheritdoc />
    public ProviderHealthStatus GetStatus(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        lock (_sync)
        {
            if (_providerStatuses.TryGetValue(providerName, out var status))
            {
                return status;
            }

            var initial = CreateInitialStatus(providerName);
            _providerStatuses[providerName] = initial;
            _metrics.SetProviderHealth(_providerStatuses);
            return initial;
        }
    }

    /// <inheritdoc />
    public void RecordProbeResult(string providerName, ProviderProbeResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(result);

        ProviderHealthStatus updatedStatus;
        lock (_sync)
        {
            var current = _providerStatuses.TryGetValue(providerName, out var existing)
                ? existing
                : CreateInitialStatus(providerName);
            updatedStatus = UpdateStatus(current, result);
            _providerStatuses[providerName] = updatedStatus;
            _metrics.SetProviderHealth(_providerStatuses);
        }

        _logger.LogDebug(
            "Provider {ProviderName} health recorded as {State} (failures={Failures}, successes={Successes})",
            providerName,
            updatedStatus.State,
            updatedStatus.ConsecutiveFailures,
            updatedStatus.ConsecutiveSuccesses);
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        foreach (var probe in _probes.Values)
        {
            ProviderProbeResult result;
            try
            {
                result = await probe.ProbeAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Provider-health probe failed unexpectedly for {ProviderName}", probe.ProviderName);
                result = ProviderProbeResult.Unhealthy("Provider probe failed unexpectedly.", ex.Message);
            }

            RecordProbeResult(probe.ProviderName, result);
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SeedKnownProviders();
        await RefreshAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _config.HealthTracking.CheckIntervalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RefreshAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private void SeedKnownProviders()
    {
        lock (_sync)
        {
            foreach (var providerName in _probes.Keys)
            {
                if (!_providerStatuses.ContainsKey(providerName))
                {
                    _providerStatuses[providerName] = CreateInitialStatus(providerName);
                }
            }

            _metrics.SetProviderHealth(_providerStatuses);
        }
    }

    private ProviderHealthSnapshot CreateSnapshotLocked()
        => new()
        {
            Providers = new Dictionary<string, ProviderHealthStatus>(_providerStatuses, StringComparer.OrdinalIgnoreCase)
        };

    private ProviderHealthStatus CreateInitialStatus(string providerName)
        => new()
        {
            ProviderName = providerName,
            State = ProviderHealthState.Healthy,
            Description = "Provider has not yet been probed.",
            LastCheckedAt = _timeProvider.GetUtcNow(),
        };

    private ProviderHealthStatus UpdateStatus(ProviderHealthStatus current, ProviderProbeResult result)
    {
        var now = _timeProvider.GetUtcNow();
        var healthyThreshold = Math.Max(1, _config.HealthTracking.HealthyThreshold);
        var unhealthyThreshold = Math.Max(1, _config.HealthTracking.UnhealthyThreshold);

        if (result.IsHealthy)
        {
            var consecutiveSuccesses = current.ConsecutiveSuccesses + 1;
            var nextState = current.State == ProviderHealthState.Healthy || consecutiveSuccesses >= healthyThreshold
                ? ProviderHealthState.Healthy
                : current.State;

            return current with
            {
                State = nextState,
                Description = result.Description,
                LastError = null,
                ConsecutiveFailures = 0,
                ConsecutiveSuccesses = consecutiveSuccesses,
                LastCheckedAt = now,
            };
        }

        var consecutiveFailures = current.ConsecutiveFailures + 1;
        var updatedState = current.State == ProviderHealthState.Unhealthy || consecutiveFailures >= unhealthyThreshold
            ? ProviderHealthState.Unhealthy
            : current.State;

        return current with
        {
            State = updatedState,
            Description = result.Description,
            LastError = result.ErrorMessage,
            ConsecutiveFailures = consecutiveFailures,
            ConsecutiveSuccesses = 0,
            LastCheckedAt = now,
        };
    }
}
