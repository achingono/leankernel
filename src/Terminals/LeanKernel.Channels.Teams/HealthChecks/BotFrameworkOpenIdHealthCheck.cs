using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels.Teams.HealthChecks;

/// <summary>Health check that verifies the Bot Framework OpenID metadata endpoint is reachable.</summary>
public sealed class BotFrameworkOpenIdHealthCheck(IHttpClientFactory httpClientFactory, IOptions<BotSettings> settings) : IHealthCheck
{
    /// <summary>The named HTTP client used by this health check.</summary>
    public const string HttpClientName = "bot-openid-health";

    /// <summary>Executes the health check.</summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A health check result indicating whether the OpenID metadata endpoint is reachable.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(settings.Value.OpenIdMetadataUrl, UriKind.Absolute, out var metadataUri))
        {
            return HealthCheckResult.Unhealthy("Bot OpenIdMetadataUrl is not configured.");
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(metadataUri, cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"Bot OpenID metadata responded {(int)response.StatusCode}.")
                : HealthCheckResult.Unhealthy($"Bot OpenID metadata returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Bot OpenID metadata is unreachable.", ex);
        }
    }
}