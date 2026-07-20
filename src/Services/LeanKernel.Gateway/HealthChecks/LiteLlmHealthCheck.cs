using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LeanKernel.Gateway.HealthChecks;

/// <summary>
/// Verifies connectivity to the LiteLLM proxy by polling its /health/liveliness endpoint.
/// The health URL is derived from <see cref="OpenAISettings.BaseUrl"/> by stripping the
/// trailing <c>/v1</c> path segment, if present.
/// </summary>
public sealed class LiteLlmHealthCheck : IHealthCheck
{
    internal const string HttpClientName = "litellm-health";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAISettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiteLlmHealthCheck"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create the health probe HTTP client.</param>
    /// <param name="settings">The OpenAI-compatible endpoint configuration.</param>
    public LiteLlmHealthCheck(IHttpClientFactory httpClientFactory, IOptions<OpenAISettings> settings)
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
            return HealthCheckResult.Degraded("LiteLLM BaseUrl is not configured.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var url = BuildHealthUrl(_settings.BaseUrl);
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"LiteLLM responded {(int)response.StatusCode}.")
                : HealthCheckResult.Unhealthy($"LiteLLM returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("LiteLLM is unreachable.", ex);
        }
    }

    /// <summary>
    /// Derives the LiteLLM health endpoint URL from the configured OpenAI-compatible base URL
    /// by stripping the <c>/v1</c> path suffix, then appending <c>/health/liveliness</c>.
    /// </summary>
    /// <param name="baseUrl">The configured OpenAI-compatible base URL.</param>
    /// <returns>The resolved LiteLLM health probe URL.</returns>
    internal static string BuildHealthUrl(string baseUrl)
    {
        var root = baseUrl.TrimEnd('/');
        if (root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            root = root[..^3];
        }

        return $"{root}/health/liveliness";
    }
}