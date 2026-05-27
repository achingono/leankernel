using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools.BuiltIn.FileSystem;

public static class FileSearchTool
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;
    private const int DefaultMaxDepth = 8;
    private const int DefaultMaxScannedFiles = 5_000;
    private const long DefaultMaxFileBytes = 1_000_000;

    [SuppressMessage("Major Code Smell", "S3776", Justification = "Search loop is intentionally explicit to keep safety and truncation rules readable.")]
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "file_search",
            Description = "Search local data files by filename/path or text content",
            Category = "filesystem",
            Parameters =
            [
                new ToolParameter { Name = "query", Type = "string", Required = false },
                new ToolParameter { Name = "name", Type = "string", Required = false },
                new ToolParameter { Name = "content", Type = "string", Required = false },
                new ToolParameter { Name = "path", Type = "string", Required = false },
                new ToolParameter { Name = "limit", Type = "integer", Required = false },
                new ToolParameter { Name = "maxDepth", Type = "integer", Required = false },
                new ToolParameter { Name = "maxScannedFiles", Type = "integer", Required = false },
                new ToolParameter { Name = "maxFileBytes", Type = "integer", Required = false },
                new ToolParameter { Name = "includeHidden", Type = "boolean", Required = false }
            ],
            Handler = async (args, ct) =>
            {
                var search = ParseParameters(args);
                if (string.IsNullOrWhiteSpace(search.Query) && string.IsNullOrWhiteSpace(search.Name) && string.IsNullOrWhiteSpace(search.Content))
                {
                    return new ToolResult { ToolName = "file_search", Success = false, Error = "A query, name, or content search term is required." };
                }

                using var scope = scopeFactory.CreateScope();
                var config = scope.ServiceProvider.GetRequiredService<IOptions<LeanKernelConfig>>().Value.FileSystem;
                var startPath = FileSystemSupport.ResolveWithinRoot(config.AllowedRoot, search.Path);
                if (startPath is null)
                {
                    return new ToolResult { ToolName = "file_search", Success = false, Error = "Access denied: path is outside the allowed directory" };
                }

                if (!Directory.Exists(startPath))
                {
                    return new ToolResult { ToolName = "file_search", Success = false, Error = $"Directory not found: {search.Path}" };
                }

                var response = await SearchAsync(startPath, config.AllowedRoot, search, ct);
                return new ToolResult { ToolName = "file_search", Success = true, Output = JsonSerializer.Serialize(response) };
            }
        };
    }

    [SuppressMessage("Major Code Smell", "S3776", Justification = "Search loop is intentionally explicit to keep safety and truncation rules readable.")]
    private static async Task<Response> SearchAsync(string startPath, string rootPath, SearchParameters parameters, CancellationToken ct)
    {
        var results = new List<Match>();
        var stats = new Stats();
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((startPath, 0));

        while (stack.Count > 0 && !stats.Truncated)
        {
            ct.ThrowIfCancellationRequested();
            var (directory, depth) = stack.Pop();

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory);
            }
            catch
            {
                stats.SkippedEntries++;
                continue;
            }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entry);
                if (!parameters.IncludeHidden && name.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                if (Directory.Exists(entry))
                {
                    stats.ScannedDirectories++;
                    var matchedOn = GetPathMatches(entry, rootPath, parameters);
                    if (matchedOn.Count > 0)
                    {
                        results.Add(CreateMatch(entry, rootPath, "directory", matchedOn, parameters));
                    }

                    if (depth < parameters.MaxDepth)
                    {
                        stack.Push((entry, depth + 1));
                    }

                    continue;
                }

                if (!File.Exists(entry))
                {
                    continue;
                }

                if (stats.ScannedFiles >= parameters.MaxScannedFiles)
                {
                    stats.Truncated = true;
                    break;
                }

                stats.ScannedFiles++;
                var matched = GetPathMatches(entry, rootPath, parameters);

                if (ShouldSearchContent(parameters, stats))
                {
                    var content = await TryReadContentAsync(entry, parameters, ct);
                    if (content is not null)
                    {
                        matched.Add("content");
                        if (!string.IsNullOrWhiteSpace(content.Snippet))
                        {
                            matched.Add("snippet");
                        }

                        if (!string.IsNullOrWhiteSpace(content.Snippet))
                        {
                            results.Add(CreateMatch(entry, rootPath, "file", matched, parameters, content.Snippet));
                            continue;
                        }
                    }
                }

                if (matched.Count > 0)
                {
                    results.Add(CreateMatch(entry, rootPath, "file", matched, parameters));
                }
            }
        }

        return new Response(
            Results: results
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
                .Take(parameters.Limit)
                .ToList(),
            ScannedFiles: stats.ScannedFiles,
            ScannedDirectories: stats.ScannedDirectories,
            SkippedEntries: stats.SkippedEntries,
            Truncated: stats.Truncated);
    }

    private static async Task<ContentResult?> TryReadContentAsync(string path, SearchParameters parameters, CancellationToken ct)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > parameters.MaxFileBytes)
            {
                return null;
            }

            if (FileSystemSupport.IsTextLikeExtension(path))
            {
                var text = await File.ReadAllTextAsync(path, ct);
                var term = !string.IsNullOrWhiteSpace(parameters.Content) ? parameters.Content : parameters.Query;
                var index = text.IndexOf(term ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                return index < 0 ? null : new ContentResult(CreateSnippet(text, index, term!.Length));
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static List<string> GetPathMatches(string fullPath, string rootPath, SearchParameters parameters)
    {
        var matchedOn = new List<string>();
        var relativePath = Path.GetRelativePath(rootPath, fullPath).Replace('\\', '/');
        var name = Path.GetFileName(fullPath);

        if (Contains(relativePath, parameters.Query) || Contains(name, parameters.Query))
        {
            matchedOn.Add("name");
        }

        if (Contains(relativePath, parameters.Name) || Contains(name, parameters.Name))
        {
            matchedOn.Add("name");
        }

        return matchedOn.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Match CreateMatch(string fullPath, string rootPath, string kind, IReadOnlyList<string> matchedOn, SearchParameters parameters, string? snippet = null)
    {
        var name = Path.GetFileName(fullPath);
        var relative = Path.GetRelativePath(rootPath, fullPath).Replace('\\', '/');
        var sizeBytes = kind == "file" ? new FileInfo(fullPath).Length : (long?)null;
        var modifiedUtc = kind == "file" ? File.GetLastWriteTimeUtc(fullPath) : Directory.GetLastWriteTimeUtc(fullPath);

        return new Match(relative, name, kind, sizeBytes, modifiedUtc, ScoreMatch(relative, kind, matchedOn, parameters), matchedOn, snippet);
    }

    private static int ScoreMatch(string relativePath, string kind, IReadOnlyList<string> matchedOn, SearchParameters parameters)
    {
        var score = kind == "file" ? 10 : 0;
        if (matchedOn.Contains("name", StringComparer.OrdinalIgnoreCase)) score += 50;
        if (matchedOn.Contains("content", StringComparer.OrdinalIgnoreCase)) score += 40;
        if (!string.IsNullOrWhiteSpace(parameters.Path) && relativePath.StartsWith(FileSystemSupport.NormalizeRelativePath(parameters.Path) + "/", StringComparison.OrdinalIgnoreCase)) score += 20;
        return score;
    }

    private static bool ShouldSearchContent(SearchParameters parameters, Stats stats)
    {
        return stats.ScannedFiles < parameters.MaxScannedFiles && (!string.IsNullOrWhiteSpace(parameters.Query) || !string.IsNullOrWhiteSpace(parameters.Content));
    }

    private static string CreateSnippet(string text, int index, int length)
    {
        const int context = 80;
        var start = Math.Max(0, index - context);
        var end = Math.Min(text.Length, index + length + context);
        return text[start..end].Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
    }

    private static bool Contains(string value, string? term)
    {
        return !string.IsNullOrWhiteSpace(term) && value.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private static SearchParameters ParseParameters(IDictionary<string, object?> args)
    {
        return new SearchParameters(
            Query: ToolArgumentReader.GetString(args, "query"),
            Name: ToolArgumentReader.GetString(args, "name"),
            Content: ToolArgumentReader.GetString(args, "content"),
            Path: ToolArgumentReader.GetString(args, "path"),
            Limit: Math.Clamp(ToolArgumentReader.GetInt32OrDefault(args, "limit", DefaultLimit), 1, MaxLimit),
            MaxDepth: Math.Clamp(ToolArgumentReader.GetInt32OrDefault(args, "maxDepth", DefaultMaxDepth), 0, 32),
            MaxScannedFiles: Math.Clamp(ToolArgumentReader.GetInt32OrDefault(args, "maxScannedFiles", DefaultMaxScannedFiles), 1, 50_000),
            MaxFileBytes: Math.Clamp(ToolArgumentReader.GetInt32OrDefault(args, "maxFileBytes", (int)DefaultMaxFileBytes), 1, 20_000_000),
            IncludeHidden: ToolArgumentReader.GetBoolOrDefault(args, "includeHidden", false));
    }

    private sealed record SearchParameters(string Query, string Name, string Content, string Path, int Limit, int MaxDepth, int MaxScannedFiles, long MaxFileBytes, bool IncludeHidden);

    private sealed record ContentResult(string Snippet);

    private sealed record Response(IReadOnlyList<Match> Results, int ScannedFiles, int ScannedDirectories, int SkippedEntries, bool Truncated);

    private sealed record Match(string Path, string Name, string Kind, long? SizeBytes, DateTime ModifiedUtc, int Score, IReadOnlyList<string> MatchedOn, string? Snippet);

    private sealed record Stats
    {
        public int ScannedFiles { get; set; }
        public int ScannedDirectories { get; set; }
        public int SkippedEntries { get; set; }
        public bool Truncated { get; set; }
    }
}
