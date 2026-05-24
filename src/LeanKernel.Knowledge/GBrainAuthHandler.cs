using System.Net.Http.Headers;
using LeanKernel.Abstractions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Knowledge;

/// <summary>
/// DelegatingHandler that resolves the GBrain bearer token from config or
/// the shared token file written by the GBrain start script.
/// </summary>
public sealed class GBrainAuthHandler : DelegatingHandler
{
    private readonly GBrainConfig _config;
    private readonly ILogger<GBrainAuthHandler> _logger;
    private string? _cachedToken;

    internal const string TokenFilePath = "/app/data/gbrain/.engine-token";

    public GBrainAuthHandler(
        IOptions<GBrainConfig> config,
        ILogger<GBrainAuthHandler> logger)
    {
        _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = ResolveToken();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // GBrain MCP requires both accept types per HTTP Streamable Transport spec
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private string? ResolveToken()
    {
        // Prefer explicit config
        if (!string.IsNullOrWhiteSpace(_config.AuthToken))
        {
            return _config.AuthToken;
        }

        // Use cached token from file
        if (_cachedToken is not null)
        {
            return _cachedToken;
        }

        // Try reading from shared volume token file
        try
        {
            if (File.Exists(TokenFilePath))
            {
                _cachedToken = File.ReadAllText(TokenFilePath).Trim();
                _logger.LogInformation("Loaded GBrain auth token from shared volume");
                return _cachedToken;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read GBrain token file at {Path}", TokenFilePath);
        }

        return null;
    }
}
