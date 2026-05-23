using LeanKernel.Abstractions.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LeanKernel.Diagnostics.Health;

/// <summary>
/// Exposes tracked provider health through ASP.NET Core health checks.
/// </summary>
public sealed class ProviderHealthCheck(IProviderHealthTracker providerHealthTracker) : IHealthCheck
{
    private readonly IProviderHealthTracker _providerHealthTracker = providerHealthTracker ?? throw new ArgumentNullException(nameof(providerHealthTracker));

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = _providerHealthTracker.GetSnapshot();
        var data = snapshot.Providers.ToDictionary(
            pair => pair.Key,
            pair => (object)new
            {
                state = pair.Value.State.ToString(),
                description = pair.Value.Description,
                lastError = pair.Value.LastError,
                failures = pair.Value.ConsecutiveFailures,
                successes = pair.Value.ConsecutiveSuccesses,
                lastCheckedAt = pair.Value.LastCheckedAt,
            },
            StringComparer.OrdinalIgnoreCase);

        if (snapshot.AllHealthy)
        {
            return Task.FromResult(HealthCheckResult.Healthy("All tracked providers are healthy.", data));
        }

        var unhealthyProviders = snapshot.Providers.Values
            .Where(status => !status.IsHealthy)
            .Select(status => status.ProviderName)
            .ToArray();

        return Task.FromResult(new HealthCheckResult(
            context.Registration.FailureStatus,
            description: $"Unhealthy providers: {string.Join(", ", unhealthyProviders)}.",
            data: data));
    }
}
