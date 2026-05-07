using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeanKernel.Host.Models.Routing;

namespace LeanKernel.Host.Services;

public sealed class ModelLimitDriftService : IModelLimitDriftService
{
    private readonly string _scriptPath;
    private readonly string _configPath;
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public ModelLimitDriftService(string scriptPath, string configPath)
    {
        _scriptPath = scriptPath;
        _configPath = configPath;
    }

    public async Task<DriftReport> PreviewDriftAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_scriptPath))
            return new DriftReport(DateTime.UtcNow.ToString("O"), 0, [])
            {
                Error = $"Drift script not found: {_scriptPath}"
            };

        var reportFile = Path.Combine(Path.GetTempPath(), $"LeanKernel-drift-{Guid.NewGuid():N}.json");
        try
        {
            var psi = new ProcessStartInfo("python3", $"\"{_scriptPath}\" --config \"{_configPath}\" --drift-report \"{reportFile}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return Error("Failed to start python3 process");

            await process.WaitForExitAsync(ct);

            if (!File.Exists(reportFile))
            {
                var stderr = await process.StandardError.ReadToEndAsync(ct);
                return Error($"Drift script produced no report. Exit code: {process.ExitCode}. {stderr.Trim()}");
            }

            var json = await File.ReadAllTextAsync(reportFile, ct);
            var raw = JsonSerializer.Deserialize<RawDriftReport>(json, _jsonOptions);
            if (raw is null)
                return Error("Failed to parse drift report JSON");

            var entries = raw.Changes?
                .Select(c => new DriftEntry(
                    c.Provider ?? "",
                    c.ModelId ?? "",
                    c.ModelName ?? "",
                    c.Field ?? "",
                    ToDouble(c.OldValue),
                    ToDouble(c.NewValue)))
                .ToList() ?? [];

            return new DriftReport(raw.GeneratedAt ?? DateTime.UtcNow.ToString("O"), raw.TotalChanges, entries);
        }
        finally
        {
            if (File.Exists(reportFile))
                File.Delete(reportFile);
        }
    }

    private static DriftReport Error(string message) =>
        new(DateTime.UtcNow.ToString("O"), 0, []) { Error = message };

    private static double? ToDouble(object? value) =>
        value switch
        {
            null => null,
            double d => d,
            long l => l,
            int i => i,
            JsonElement e when e.ValueKind == JsonValueKind.Number => e.TryGetDouble(out var d) ? d : null,
            _ => null
        };

    // Raw JSON deserialization shapes
    private sealed record RawDriftReport(
        [property: JsonPropertyName("generated_at")] string? GeneratedAt,
        [property: JsonPropertyName("total_changes")] int TotalChanges,
        [property: JsonPropertyName("changes")] List<RawDriftEntry>? Changes);

    private sealed record RawDriftEntry(
        [property: JsonPropertyName("provider")] string? Provider,
        [property: JsonPropertyName("model_id")] string? ModelId,
        [property: JsonPropertyName("model_name")] string? ModelName,
        [property: JsonPropertyName("field")] string? Field,
        [property: JsonPropertyName("old_value")] object? OldValue,
        [property: JsonPropertyName("new_value")] object? NewValue);
}
