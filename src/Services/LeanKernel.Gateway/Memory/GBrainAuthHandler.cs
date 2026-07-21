using System.Net.Http.Headers;

using LeanKernel.Gateway.Configuration;

using Microsoft.Extensions.Options;

namespace LeanKernel.Gateway.Memory;

/// <summary>
/// DelegatingHandler that resolves the GBrain bearer token from config or
/// the shared token file written by the GBrain start script.
/// </summary>
public sealed class GBrainAuthHandler : DelegatingHandler
{
    private static readonly string[] TokenFileCandidates = [TokenFilePath, "/run/secrets/gbrain_auth_token"];

    private readonly GBrainSettings _config;
    private readonly ILogger<GBrainAuthHandler> _logger;
    private string? _cachedToken;

    internal const string TokenFilePath = "/app/data/gbrain/.engine-token";

    /// <summary>
    /// Initializes a new instance of the <see cref="GBrainAuthHandler"/> class.
    /// </summary>
    /// <param name="config">Provides the configured GBrain settings.</param>
    /// <param name="logger">The logger for token resolution diagnostics.</param>
    public GBrainAuthHandler(
        IOptions<GBrainSettings> config,
        ILogger<GBrainAuthHandler> logger)
    {
        _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = ResolveToken();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(Constants.Http.Headers.Bearer, token);
        }

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(Constants.ContentTypes.Json));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the bearer token from shared token files or configured settings.
    /// </summary>
    /// <returns>The resolved bearer token, or <c>null</c> when no token is available.</returns>
    private string? ResolveToken()
    {
        foreach (var tokenPath in TokenFileCandidates)
        {
            var token = TryReadTokenFile(tokenPath);
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
        }

        if (!string.IsNullOrWhiteSpace(_config.AuthToken))
        {
            return _config.AuthToken;
        }

        if (_cachedToken is not null)
        {
            return _cachedToken;
        }

        return null;
    }

    /// <summary>
    /// Attempts to read and cache a bearer token from the specified file.
    /// </summary>
    /// <param name="path">The token file path to inspect.</param>
    /// <returns>The token value, or <c>null</c> when the file is missing, empty, or unreadable.</returns>
    private string? TryReadTokenFile(string path)
    {
        if (_cachedToken is not null)
        {
            return _cachedToken;
        }

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var token = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            _cachedToken = token;
            _logger.LogInformation("Loaded GBrain auth token from {Path}", path);
            return _cachedToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read GBrain token file at {Path}", path);
            return null;
        }
    }
}