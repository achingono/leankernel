using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels.Teams.HealthChecks;

public sealed class BotFrameworkOpenIdHealthCheck(IHttpClientFactory httpClientFactory, IOptions<BotSettings> settings) : IHealthCheck
{
    public const string HttpClientName = "bot-openid-health";

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