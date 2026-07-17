using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels.Signal.HealthChecks;

public sealed class SignalApiHealthCheck(IHttpClientFactory httpClientFactory, IOptions<SignalSettings> settings) : IHealthCheck
{
    public const string HttpClientName = "signal-api-health";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.Value.Host) || settings.Value.Port <= 0)
            return HealthCheckResult.Unhealthy("Signal Host/Port is not configured.");

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var signalApiUri = new UriBuilder(Uri.UriSchemeHttp, settings.Value.Host, settings.Value.Port, "/v1/about").Uri;
            using var response = await client.GetAsync(signalApiUri, cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"Signal API responded {(int)response.StatusCode}.")
                : HealthCheckResult.Unhealthy($"Signal API returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Signal API is unreachable.", ex);
        }
    }
}
