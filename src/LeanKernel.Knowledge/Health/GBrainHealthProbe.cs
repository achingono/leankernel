using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Knowledge.Health;

/// <summary>
/// Probes the GBrain HTTP health endpoint.
/// </summary>
public sealed class GBrainHealthProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<GBrainConfig> config,
    ILogger<GBrainHealthProbe> logger) : IProviderHealthProbe
{
    /// <summary>
    /// The named HTTP client used for GBrain health checks.
    /// </summary>
    public const string HttpClientName = "LeanKernel.GBrain.Health";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly GBrainConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
    private readonly ILogger<GBrainHealthProbe> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public string ProviderName => ProviderNames.GBrain;

    /// <inheritdoc />
    public async Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClientFactory.CreateClient(HttpClientName)
                .GetAsync(BuildHealthUri(), ct)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? ProviderProbeResult.Healthy("GBrain health probe succeeded.")
                : ProviderProbeResult.Unhealthy($"GBrain health probe returned {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GBrain health probe failed");
            return ProviderProbeResult.Unhealthy("GBrain health probe failed.", ex.Message);
        }
    }

    private Uri BuildHealthUri()
    {
        var baseUri = new Uri(_config.BaseUrl, UriKind.Absolute);
        return new Uri(baseUri, "/health");
    }
}
