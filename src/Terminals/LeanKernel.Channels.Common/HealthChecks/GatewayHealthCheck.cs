using LeanKernel.Channels.Common.Configuration;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels.Common.HealthChecks;

public sealed class GatewayHealthCheck(IHttpClientFactory httpClientFactory, IOptions<GatewaySettings> settings) : IHealthCheck
{
    public const string HttpClientName = "gateway-health";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(settings.Value.BaseUrl, UriKind.Absolute, out var gatewayBaseUri))
        {
            return HealthCheckResult.Unhealthy("Gateway BaseUrl is not configured.");
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var healthUri = new Uri(gatewayBaseUri, "/health");
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