using LeanKernel.Logic.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.BuiltIn.Browser;

/// <summary>
/// Probes authenticated readiness for the browser automation sidecar.
/// </summary>
public sealed class WebwrightHealthProbe : IProviderHealthProbe
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebwrightSettings _settings;
    private readonly ILogger<WebwrightHealthProbe> _logger;

    public WebwrightHealthProbe(
        IHttpClientFactory httpClientFactory,
        IOptions<AgentSettings> options,
        ILogger<WebwrightHealthProbe> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _settings = (options ?? throw new ArgumentNullException(nameof(options))).Value.Tools.Webwright;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => ProviderNames.Webwright;

    public async Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        if (!_settings.Enabled || !_settings.HealthProbe.Enabled)
        {
            return ProviderProbeResult.Healthy("Browser service health probe is disabled.");
        }

        try
        {
            using var response = await _httpClientFactory.CreateClient(WebwrightClient.HttpClientName)
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
            _logger.LogWarning(ex, "Browser service health probe failed against {BaseUrl}", _settings.BaseUrl);
            return ProviderProbeResult.Unhealthy("Browser service health probe failed.", ex.Message);
        }
    }
}
