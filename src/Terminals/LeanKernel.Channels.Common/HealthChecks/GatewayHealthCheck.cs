using LeanKernel.Channels.Common.Configuration;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels.Common.HealthChecks;

/// <summary>Health check that probes the Gateway's /health endpoint.</summary>
public sealed class GatewayHealthCheck(IHttpClientFactory httpClientFactory, IOptions<GatewaySettings> settings) : IHealthCheck
{
    /// <summary>Named HTTP client used for health-check requests.</summary>
    public const string HttpClientName = "gateway-health";

    /// <summary>Executes the health check by calling the Gateway /health endpoint.</summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="HealthCheckResult"/> indicating healthy, degraded, or unhealthy status.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(settings.Value.BaseUrl, UriKind.Absolute, out var gatewayBaseUri))
        {
            return HealthCheckResult.Unhealthy("Gateway BaseUrl is not configured.");
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var healthUri = new Uri(gatewayBaseUri, Constants.Healthchecks.Path);
            using var response = await client.GetAsync(healthUri, cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"Gateway responded {(int)response.StatusCode}.")
                : HealthCheckResult.Unhealthy($"Gateway returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Gateway is unreachable.", ex);
        }
    }
}