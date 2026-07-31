namespace LeanKernel.Services.Gateway.Providers;

using LeanKernel.Services.Gateway.Configuration;

using Microsoft.Extensions.Options;

/// <summary>
/// Middleware that applies hardening measures such as correlation ID injection and API key enforcement.
/// </summary>
/// <param name="next">The next middleware in the pipeline.</param>
public sealed class HardeningMiddleware(RequestDelegate next)
{
    private static readonly PathString[] ProtectedPrefixes = [new PathString("/v1/diagnostics")];

    /// <summary>
    /// Invokes the middleware, adding correlation IDs and optionally validating API keys for protected paths.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="settings">The hardening settings.</param>
    /// <param name="logger">The logger instance.</param>
    /// <returns>A task that represents the completion of request processing.</returns>
    public async Task InvokeAsync(HttpContext context, IOptions<HardeningSettings> settings, ILogger<HardeningMiddleware> logger)
    {
        var hardening = settings.Value;
        var correlationId = GetCorrelationId(context, hardening.CorrelationIdHeader);
        context.Items[CorrelationIdKey] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.Headers[hardening.CorrelationIdHeader] = correlationId;

        if (RequiresApiKey(context.Request.Path) && hardening.RequireApiKey && !HasValidApiKey(context.Request, hardening))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            logger.LogWarning("Request to {Path} rejected: missing or invalid API key.", context.Request.Path);
            return;
        }

        await next(context);
    }

    /// <summary>
    /// The key used to store the correlation ID in <see cref="HttpContext.Items"/>.
    /// </summary>
    internal const string CorrelationIdKey = "LK.CorrelationId";

    private static string GetCorrelationId(HttpContext context, string headerName)
    {
        if (context.Request.Headers.TryGetValue(headerName, out var values) && !string.IsNullOrWhiteSpace(values.ToString()))
        {
            return values.ToString();
        }

        return Guid.NewGuid().ToString("N");
    }

    private static bool HasValidApiKey(HttpRequest request, HardeningSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return false;
        }

        if (!request.Headers.TryGetValue(settings.ApiKeyHeader, out var keyValues))
        {
            return false;
        }

        return string.Equals(keyValues.ToString(), settings.ApiKey, StringComparison.Ordinal);
    }

    private static bool RequiresApiKey(PathString path) => ProtectedPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}