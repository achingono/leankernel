using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools.BuiltIn.Browser;

/// <summary>
/// Probes authenticated readiness for the browser automation sidecar.
/// </summary>
public sealed class BrowserServiceHealthProbe : IProviderHealthProbe
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<BrowserServiceHealthProbe> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserServiceHealthProbe"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="config">The LeanKernel configuration.</param>
    /// <param name="logger">The logger.</param>
    public BrowserServiceHealthProbe(
        IHttpClientFactory httpClientFactory,
        IOptions<LeanKernelConfig> config,
        ILogger<BrowserServiceHealthProbe> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ProviderName => ProviderNames.BrowserService;

    /// <inheritdoc />
    public async Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        if (!_config.BrowserService.Enabled || !_config.BrowserService.HealthProbe.Enabled)
        {
            return ProviderProbeResult.Healthy("Browser service health probe is disabled.");
        }

        try
        {
            using var response = await _httpClientFactory.CreateClient(BrowserServiceClient.HttpClientName)
                .GetAsync("ready", ct)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? ProviderProbeResult.Healthy("Browser service readiness probe succeeded.")
                : ProviderProbeResult.Unhealthy($"Browser service readiness probe returned {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Browser service health probe failed against {BaseUrl}", _config.BrowserService.BaseUrl);
            return ProviderProbeResult.Unhealthy("Browser service health probe failed.", ex.Message);
        }
    }
}
