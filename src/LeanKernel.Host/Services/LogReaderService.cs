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
        var filter = LogSearchFilter.Create(query, level, since);

        foreach (var file in files)
        {
            var fullPath = Path.Combine(_logDirectory, file);
            if (!File.Exists(fullPath))
                continue;

            await AddMatchingLinesAsync(fullPath, file, filter, limit, lines, ct);
            if (HasReachedLimit(lines, limit))
                break;
        }

        return new LogSearchResult
        {
            Lines = lines,
            TotalFiles = files.Count
        };
    }

    private static async Task AddMatchingLinesAsync(
        string fullPath,
        string file,
        LogSearchFilter filter,
        int limit,
        List<LogLine> results,
        CancellationToken ct)
    {
        var fileLines = await File.ReadAllLinesAsync(fullPath, ct);
        foreach (var line in fileLines)
        {
            if (!ShouldIncludeLine(line, filter))
                continue;

            results.Add(new LogLine { Content = line, File = file });
            if (HasReachedLimit(results, limit))
                break;
        }
    }

    private static bool ShouldIncludeLine(string line, LogSearchFilter filter)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        if (!string.IsNullOrEmpty(filter.Level) &&
            !line.Contains(filter.Level, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(filter.Query) &&
            !line.Contains(filter.Query, StringComparison.OrdinalIgnoreCase))
            return false;

        return IsAfterSince(line, filter.Since);
    }

    private static bool IsAfterSince(string line, DateTimeOffset? since)
    {
        if (!since.HasValue)
            return true;

        var timestamp = ExtractTimestamp(line);
        return !timestamp.HasValue || timestamp.Value >= since.Value;
    }

    private static bool HasReachedLimit(List<LogLine> lines, int limit) => lines.Count >= limit;

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

    private sealed record LogSearchFilter(string? Query, string? Level, DateTimeOffset? Since)
    {
        public static LogSearchFilter Create(string? query, string? level, string? since)
        {
            var parsedSince = DateTimeOffset.TryParse(since, out var sinceDate)
                ? sinceDate
                : (DateTimeOffset?)null;

            return new LogSearchFilter(query, level?.ToUpperInvariant(), parsedSince);
        }
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
