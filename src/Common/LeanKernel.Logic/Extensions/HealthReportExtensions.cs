namespace Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Provides extension methods for registering LeanKernel logic services.
/// </summary>
public static class HealthReportExtensions
{
    /// <summary>
    /// Serializes a <see cref="HealthReport"/> to a JSON string.
    /// </summary>
    /// <param name="report">The health report to serialize.</param>
    /// <returns>A JSON string representing the health report.</returns>
    public static string ToJson(this HealthReport report)
    {
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

        return System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }
}