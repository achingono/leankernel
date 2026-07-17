using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LeanKernel.Channels.Common.HealthChecks;

public static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

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

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions));
    }
}
