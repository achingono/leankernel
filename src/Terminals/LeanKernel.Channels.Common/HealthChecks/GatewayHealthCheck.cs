using LeanKernel.Channels.Common.Settings;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels.Common.HealthChecks;

/// <summary>
/// Health check that probes the gateway's /health endpoint.
/// </summary>
public sealed class GatewayHealthCheck(IHttpClientFactory httpClientFactory, IOptions<GatewaySettings> settings) : IHealthCheck
{
    /// <summary>
    /// Named <see cref="HttpClient"/> identifier used for gateway health probes.
    /// </summary>
    public const string HttpClientName = "gateway-health";

    /// <summary>
    /// Executes the health check by calling the gateway's /health endpoint.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="HealthCheckResult"/> indicating the gateway status.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(settings.Value.BaseUrl, UriKind.Absolute, out var gatewayBaseUri))
            return HealthCheckResult.Unhealthy("Gateway BaseUrl is not configured.");

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var healthUri = new Uri(gatewayBaseUri, Constants.Http.HealthPath);
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