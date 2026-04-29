using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Host.Services;

/// <summary>
/// Reads and searches Serilog rolling log files from the data/logs directory.
/// </summary>
public sealed class LogReaderService
{
    private readonly string _logDirectory;

    public LogReaderService(IOptions<LeanKernelConfig> config)
    {
        var wikiPath = config.Value.Wiki.BasePath;
        var dataDir = Path.GetDirectoryName(wikiPath) ?? "/app/data";
        _logDirectory = Path.Combine(dataDir, "logs");
    }

    public IReadOnlyList<string> ListLogFiles()
    {
        if (!Directory.Exists(_logDirectory)) return [];

        return Directory.GetFiles(_logDirectory, "LeanKernel-*.log")
            .OrderByDescending(f => f)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Cast<string>()
            .ToList();
    }

    public async Task<LogSearchResult> SearchAsync(
        string? query = null,
        string? level = null,
        string? since = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        var lines = new List<LogLine>();
        var files = ListLogFiles();

        foreach (var file in files)
        {
            var fullPath = Path.Combine(_logDirectory, file);
            if (!File.Exists(fullPath)) continue;

            var fileLines = await File.ReadAllLinesAsync(fullPath, ct);
            foreach (var line in fileLines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Level filter
                if (!string.IsNullOrEmpty(level))
                {
                    var upperLevel = level.ToUpperInvariant();
                    if (!line.Contains(upperLevel, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Query filter
                if (!string.IsNullOrEmpty(query) &&
                    !line.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Since filter
                if (!string.IsNullOrEmpty(since) && DateTimeOffset.TryParse(since, out var sinceDate))
                {
                    var timestamp = ExtractTimestamp(line);
                    if (timestamp.HasValue && timestamp.Value < sinceDate)
                        continue;
                }

                lines.Add(new LogLine { Content = line, File = file });

                if (lines.Count >= limit) break;
            }

            if (lines.Count >= limit) break;
        }

        return new LogSearchResult
        {
            Lines = lines,
            TotalFiles = files.Count
        };
    }

    private static DateTimeOffset? ExtractTimestamp(string line)
    {
        // Serilog format: [HH:mm:ss ...] — try parsing first bracket content
        var start = line.IndexOf('[');
        var end = line.IndexOf(']');
        if (start >= 0 && end > start)
        {
            var ts = line[(start + 1)..end];
            if (TimeOnly.TryParse(ts, out _))
                return null; // Time-only, can't compare to date
        }
        return null;
    }
}

public sealed class LogSearchResult
{
    public List<LogLine> Lines { get; init; } = [];
    public int TotalFiles { get; init; }
}

public sealed class LogLine
{
    public required string Content { get; init; }
    public required string File { get; init; }
}
