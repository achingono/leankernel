using System.Diagnostics;
using Serilog.Context;

namespace LeanKernel.Gateway.Middleware;

/// <summary>
/// Ensures every request has a correlation identifier and records request metrics.
/// </summary>
public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    LeanKernel.Diagnostics.LeanKernelMetrics metrics,
    ILogger<CorrelationIdMiddleware> logger) 
{
    /// <summary>
    /// The correlation-id header name.
    /// </summary>
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly LeanKernel.Diagnostics.LeanKernelMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    private readonly ILogger<CorrelationIdMiddleware> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the request finishes.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        var startedAt = Stopwatch.GetTimestamp();
        var statusCode = StatusCodes.Status200OK;
        Exception? capturedException = null;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
        }))
        {
            try
            {
                await _next(context).ConfigureAwait(false);
                statusCode = context.Response.StatusCode;
            }
            catch (Exception ex)
            {
                capturedException = ex;
                statusCode = StatusCodes.Status500InternalServerError;
                throw;
            }
            finally
            {
                var endpoint = context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "unknown";
                var durationMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                _metrics.RecordRequestTotal(endpoint, context.Request.Method);
                _metrics.RecordRequestDuration(endpoint, context.Request.Method, statusCode, durationMs);

                if (capturedException is not null || statusCode >= StatusCodes.Status400BadRequest)
                {
                    _metrics.RecordRequestError(endpoint, context.Request.Method, statusCode);
                }
            }
        }
    }
}
