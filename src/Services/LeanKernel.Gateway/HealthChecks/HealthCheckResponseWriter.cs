using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LeanKernel.Gateway.HealthChecks;

/// <summary>
/// Writes a structured JSON health report to the HTTP response.
/// </summary>
internal static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>
    /// Writes the health report as a JSON object to the response body.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="report">The health report to serialize.</param>
    internal static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration.TotalMilliseconds,
            timestamp = DateTime.UtcNow,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds,
                tags = e.Value.Tags
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions));
    }
}
