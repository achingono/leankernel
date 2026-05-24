using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents.Health;

/// <summary>
/// Probes the LiteLLM HTTP health endpoint.
/// </summary>
public sealed class LiteLlmHealthProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<LeanKernelConfig> config,
    ILogger<LiteLlmHealthProbe> logger) : IProviderHealthProbe
{
    /// <summary>
    /// The named HTTP client used for LiteLLM health checks.
    /// </summary>
    public const string HttpClientName = "LeanKernel.LiteLlm.Health";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly LeanKernelConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
    private readonly ILogger<LiteLlmHealthProbe> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public string ProviderName => ProviderNames.LiteLlm;

    /// <inheritdoc />
    public async Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        try
        {
            using var livelinessResponse = await client.GetAsync("health/liveliness", ct).ConfigureAwait(false);
            if (livelinessResponse.IsSuccessStatusCode)
            {
                return ProviderProbeResult.Healthy("LiteLLM liveliness probe succeeded.");
            }

            using var fallbackResponse = await client.GetAsync("health", ct).ConfigureAwait(false);
            return fallbackResponse.IsSuccessStatusCode
                ? ProviderProbeResult.Healthy("LiteLLM fallback health probe succeeded.")
                : ProviderProbeResult.Unhealthy($"LiteLLM liveliness probe returned {(int)livelinessResponse.StatusCode} and fallback health probe returned {(int)fallbackResponse.StatusCode}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LiteLLM health probe failed against {BaseUrl}", _config.LiteLlm.BaseUrl);
            return ProviderProbeResult.Unhealthy("LiteLLM health probe failed.", ex.Message);
        }
    }
}
