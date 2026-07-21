using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LeanKernel.Channels.Common.HealthChecks;

/// <summary>Writes a structured JSON health-report response.</summary>
public static class HealthCheckResponseWriter
{
    /// <summary>Writes the health report as JSON to the HTTP response.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="report">The health report to serialize.</param>
    /// <returns>A task that completes when the response has been written.</returns>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration.TotalMilliseconds,
            timestamp = DateTime.UtcNow,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                duration = entry.Value.Duration.TotalMilliseconds,
                tags = entry.Value.Tags
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, Constants.Serialization.JsonOptions));
    }
}