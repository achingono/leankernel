using System.Text;
using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Searches the allowed data directory for files by path/name or text content.
/// </summary>
[ToolMetadata(
    Name = "file_search",
    Description = "Search for files by name/path or text content within the allowed data directory. Use this when knowledge search cannot locate a document that may not be indexed yet.",
    Category = ToolCategory.FileSystem)]
public sealed class FileSystemSearchTool : ITool
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;
    private const int DefaultMaxDepth = 8;
    private const int DefaultMaxScannedFiles = 5_000;
    private const long DefaultMaxFileBytes = 1_000_000;

    private readonly string _allowedBasePath;

    /// <summary>
    /// Gets the stable tool name exposed to the model.
    /// </summary>
    public string Name => "file_search";

    /// <summary>
    /// Gets the human-readable tool description exposed to the model.
    /// </summary>
    public string Description => "Search local data files by filename/path or text content as a fallback when indexed knowledge search misses a document.";

    /// <summary>
    /// Gets the category for this tool.
    /// </summary>
    public string Category => ToolCategory.FileSystem.ToString().ToLower();

    /// <summary>
    /// Gets the JSON schema describing this tool's parameters.
    /// </summary>
    public string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Search term to match against file names, relative paths, and text content" },
            "name": { "type": "string", "description": "Optional filename or relative path term to search for" },
            "content": { "type": "string", "description": "Optional text content to search for inside files" },
            "path": { "type": "string", "description": "Relative directory path to search within", "default": "" },
            "limit": { "type": "integer", "default": 20, "description": "Maximum results to return (1-100)" },
            "maxDepth": { "type": "integer", "default": 8, "description": "Maximum directory depth below the start path" },
            "maxScannedFiles": { "type": "integer", "default": 5000, "description": "Maximum number of files to inspect for content matches" },
            "maxFileBytes": { "type": "integer", "default": 1000000, "description": "Maximum file size to inspect for content matches" },
            "includeHidden": { "type": "boolean", "default": false }
          },
          "anyOf": [
            { "required": ["query"] },
            { "required": ["name"] },
            { "required": ["content"] }
          ]
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemSearchTool" /> class.
    /// </summary>
    /// <param name="allowedBasePath">The allowed base path.</param>
    public FileSystemSearchTool(string allowedBasePath = "/app/data")
    {
        _allowedBasePath = Path.GetFullPath(allowedBasePath);
    }

    /// <summary>
    /// Executes the search operation.
    /// </summary>
    /// <param name="parametersJson">The search parameters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The search result.</returns>
    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var parameters = ParseParameters(parametersJson);

            if (string.IsNullOrWhiteSpace(parameters.Query)
                && string.IsNullOrWhiteSpace(parameters.Name)
                && string.IsNullOrWhiteSpace(parameters.Content))
            {
                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = "A query, name, or content search term is required.",
                    Duration = sw.Elapsed
                };
            }

            var startPath = FileSystemPolicy.ResolveWithinBase(_allowedBasePath, parameters.Path);
            if (startPath is null)
            {
                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = "Access denied: path is outside the allowed directory",
                    Duration = sw.Elapsed
                };
            }

            if (!Directory.Exists(startPath))
            {
                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = $"Directory not found: {parameters.Path}",
                    Duration = sw.Elapsed
                };
            }

            var searchResult = await SearchAsync(startPath, parameters, ct);
            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = JsonSerializer.Serialize(searchResult),
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = ex.Message,
                Duration = sw.Elapsed
            };
        }
    }

    private async Task<FileSearchResponse> SearchAsync(string startPath, SearchParameters parameters, CancellationToken ct)
    {
        var results = new List<FileSearchMatch>();
        var stats = new FileSearchStats();
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
            catch (UnauthorizedAccessException)
            {
                stats.SkippedEntries++;
                continue;
            }
            catch (IOException)
            {
                stats.SkippedEntries++;
                continue;
            }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                var name = Path.GetFileName(entry);
                if (!parameters.IncludeHidden && name.StartsWith(".", StringComparison.Ordinal))
                    continue;

                if (IsReparsePoint(entry))
                {
                    stats.SkippedEntries++;
                    continue;
                }

                if (Directory.Exists(entry))
                {
                    stats.ScannedDirectories++;
                    var directoryMatchedOn = GetPathMatches(entry, parameters);
                    if (directoryMatchedOn.Count > 0)
                        results.Add(CreateMatch(entry, "directory", directoryMatchedOn, parameters));

                    if (depth < parameters.MaxDepth)
                        stack.Push((entry, depth + 1));

                    continue;
                }

                if (!File.Exists(entry))
                    continue;

                if (stats.ScannedFiles >= parameters.MaxScannedFiles)
                {
                    stats.Truncated = true;
                    break;
                }

                stats.ScannedFiles++;
                var matchedOn = GetPathMatches(entry, parameters);

                string? snippet = null;
                if (ShouldSearchContent(parameters, stats))
                {
                    var contentMatch = await TryFindContentMatchAsync(entry, parameters, ct);
                    if (contentMatch.SearchAttempted)
                        stats.ContentScannedFiles++;

                    if (contentMatch.Matched)
                    {
                        matchedOn.Add("content");
                        snippet = contentMatch.Snippet;
                    }
                }

                if (IsSourceDocumentCandidate(entry))
                    matchedOn.Add("source_document_candidate");

                if (matchedOn.Count > 0)
                    results.Add(CreateMatch(entry, "file", matchedOn, parameters, snippet));
            }
        }

        var orderedResults = results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .Take(parameters.Limit)
            .ToList();

        stats.Truncated = stats.Truncated || results.Count > parameters.Limit;
        return new FileSearchResponse(
            Results: orderedResults,
            ScannedFiles: stats.ScannedFiles,
            ScannedDirectories: stats.ScannedDirectories,
            ContentScannedFiles: stats.ContentScannedFiles,
            SkippedEntries: stats.SkippedEntries,
            Truncated: stats.Truncated);
    }

    private List<string> GetPathMatches(string fullPath, SearchParameters parameters)
    {
        var matchedOn = new List<string>();
        var relativePath = Path.GetRelativePath(_allowedBasePath, fullPath).Replace('\\', '/');
        var name = Path.GetFileName(fullPath);

        if (Contains(relativePath, parameters.Query) || Contains(name, parameters.Query))
            matchedOn.Add("name");

        if (Contains(relativePath, parameters.Name) || Contains(name, parameters.Name))
            matchedOn.Add("name");

        return matchedOn.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static SearchParameters ParseParameters(string parametersJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            if (doc.RootElement.ValueKind == JsonValueKind.String)
                return SearchParameters.FromPlainText(doc.RootElement.GetString() ?? string.Empty);

            return SearchParameters.FromJson(doc.RootElement);
        }
        catch (JsonException)
        {
            return SearchParameters.FromPlainText(parametersJson);
        }
    }

    private static bool ShouldSearchContent(SearchParameters parameters, FileSearchStats stats)
    {
        return stats.ContentScannedFiles < parameters.MaxScannedFiles
               && (!string.IsNullOrWhiteSpace(parameters.Query)
                   || !string.IsNullOrWhiteSpace(parameters.Content));
    }

    private async Task<ContentSearchResult> TryFindContentMatchAsync(
        string fullPath,
        SearchParameters parameters,
        CancellationToken ct)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(fullPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return ContentSearchResult.NotAttempted;
        }

        if (info.Length > parameters.MaxFileBytes)
            return ContentSearchResult.NotAttempted;

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(fullPath, ct);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return ContentSearchResult.NotAttempted;
        }

        if (bytes.Contains((byte)0))
            return ContentSearchResult.AttemptedNoMatch;

        var text = Encoding.UTF8.GetString(bytes);
        var term = !string.IsNullOrWhiteSpace(parameters.Content)
            ? parameters.Content
            : parameters.Query;

        if (string.IsNullOrWhiteSpace(term))
            return ContentSearchResult.AttemptedNoMatch;

        var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        return index < 0
            ? ContentSearchResult.AttemptedNoMatch
            : new ContentSearchResult(true, true, CreateSnippet(text, index, term.Length));
    }

    private FileSearchMatch CreateMatch(
        string fullPath,
        string kind,
        IReadOnlyList<string> matchedOn,
        SearchParameters parameters,
        string? snippet = null)
    {
        var relativePath = Path.GetRelativePath(_allowedBasePath, fullPath).Replace('\\', '/');
        var sizeBytes = kind == "file" ? new FileInfo(fullPath).Length : (long?)null;
        var modifiedUtc = kind == "file"
            ? File.GetLastWriteTimeUtc(fullPath)
            : Directory.GetLastWriteTimeUtc(fullPath);

        return new FileSearchMatch(
            Path: relativePath,
            Name: Path.GetFileName(fullPath),
            Kind: kind,
            SizeBytes: sizeBytes,
            ModifiedUtc: modifiedUtc,
            Score: ScoreMatch(relativePath, kind, matchedOn, parameters),
            MatchedOn: matchedOn,
            Snippet: snippet);
    }

    private static int ScoreMatch(
        string relativePath,
        string kind,
        IReadOnlyList<string> matchedOn,
        SearchParameters parameters)
    {
        var score = kind == "file" ? 10 : 0;
        if (matchedOn.Contains("name", StringComparer.OrdinalIgnoreCase))
            score += 50;
        if (matchedOn.Contains("content", StringComparer.OrdinalIgnoreCase))
            score += 40;
        if (matchedOn.Contains("source_document_candidate", StringComparer.OrdinalIgnoreCase))
            score += 35;
        if (relativePath.StartsWith("documents/", StringComparison.OrdinalIgnoreCase))
            score += 25;
        if (relativePath.StartsWith("wiki/llm/", StringComparison.OrdinalIgnoreCase))
            score -= 40;
        if (!string.IsNullOrWhiteSpace(parameters.Path)
            && relativePath.StartsWith(FileSystemPolicy.NormalizeRelativePath(parameters.Path) + "/", StringComparison.OrdinalIgnoreCase))
            score += 20;

        return score;
    }

    private static bool IsSourceDocumentCandidate(string fullPath)
    {
        var normalizedPath = fullPath.Replace('\\', '/');
        var extension = Path.GetExtension(fullPath);

        if (!normalizedPath.Contains("/documents/", StringComparison.OrdinalIgnoreCase))
            return false;

        return IsDocumentExtension(extension);
    }

    private static bool IsDocumentExtension(string extension)
    {
        return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".doc", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateSnippet(string text, int index, int length)
    {
        const int context = 80;
        var start = Math.Max(0, index - context);
        var end = Math.Min(text.Length, index + length + context);
        var snippet = text[start..end]
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

        return snippet.Trim();
    }

    private static bool Contains(string value, string? term)
    {
        return !string.IsNullOrWhiteSpace(term)
               && value.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    private sealed record SearchParameters(
        string Query,
        string Name,
        string Content,
        string Path,
        int Limit,
        int MaxDepth,
        int MaxScannedFiles,
        long MaxFileBytes,
        bool IncludeHidden)
    {
        public static SearchParameters FromJson(JsonElement root)
        {
            return new SearchParameters(
                Query: ReadString(root, "query"),
                Name: ReadString(root, "name"),
                Content: ReadString(root, "content"),
                Path: ReadString(root, "path"),
                Limit: Math.Clamp(ReadInt(root, "limit", DefaultLimit), 1, MaxLimit),
                MaxDepth: Math.Clamp(ReadInt(root, "maxDepth", DefaultMaxDepth), 0, 32),
                MaxScannedFiles: Math.Clamp(ReadInt(root, "maxScannedFiles", DefaultMaxScannedFiles), 1, 50_000),
                MaxFileBytes: Math.Clamp(ReadLong(root, "maxFileBytes", DefaultMaxFileBytes), 1, 20_000_000),
                IncludeHidden: root.TryGetProperty("includeHidden", out var hiddenEl) && hiddenEl.GetBoolean());
        }

        public static SearchParameters FromPlainText(string query)
        {
            return new SearchParameters(
                Query: query,
                Name: string.Empty,
                Content: string.Empty,
                Path: string.Empty,
                Limit: DefaultLimit,
                MaxDepth: DefaultMaxDepth,
                MaxScannedFiles: DefaultMaxScannedFiles,
                MaxFileBytes: DefaultMaxFileBytes,
                IncludeHidden: false);
        }

        private static string ReadString(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static int ReadInt(JsonElement root, string propertyName, int defaultValue)
        {
            return root.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
                ? result
                : defaultValue;
        }

        private static long ReadLong(JsonElement root, string propertyName, long defaultValue)
        {
            return root.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var result)
                ? result
                : defaultValue;
        }
    }

    private sealed class FileSearchStats
    {
        public int ScannedFiles { get; set; }

        public int ScannedDirectories { get; set; }

        public int ContentScannedFiles { get; set; }

        public int SkippedEntries { get; set; }

        public bool Truncated { get; set; }
    }

    private sealed record ContentSearchResult(bool SearchAttempted, bool Matched, string? Snippet)
    {
        public static ContentSearchResult NotAttempted { get; } = new(false, false, null);

        public static ContentSearchResult AttemptedNoMatch { get; } = new(true, false, null);
    }

    private sealed record FileSearchResponse(
        IReadOnlyList<FileSearchMatch> Results,
        int ScannedFiles,
        int ScannedDirectories,
        int ContentScannedFiles,
        int SkippedEntries,
        bool Truncated);

    private sealed record FileSearchMatch(
        string Path,
        string Name,
        string Kind,
        long? SizeBytes,
        DateTime ModifiedUtc,
        int Score,
        IReadOnlyList<string> MatchedOn,
        string? Snippet);
}
