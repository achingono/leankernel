using LeanKernel.Gateway.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LeanKernel.Gateway.HealthChecks;

/// <summary>
/// Verifies connectivity to the GBrain MCP service by polling its /health endpoint.
/// </summary>
public sealed class GBrainHealthCheck : IHealthCheck
{
    internal const string HttpClientName = "gbrain-health";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GBrainSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="GBrainHealthCheck"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create the health probe HTTP client.</param>
    /// <param name="settings">The GBrain configuration options.</param>
    public GBrainHealthCheck(IHttpClientFactory httpClientFactory, IOptions<GBrainSettings> settings)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _settings = (settings ?? throw new ArgumentNullException(nameof(settings))).Value;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            return HealthCheckResult.Degraded("GBrain BaseUrl is not configured.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var url = $"{_settings.BaseUrl.TrimEnd('/')}{Constants.Http.HealthPath}";
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"GBrain responded {(int)response.StatusCode}.")
                : HealthCheckResult.Unhealthy($"GBrain returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("GBrain is unreachable.", ex);
        }
    }
}

