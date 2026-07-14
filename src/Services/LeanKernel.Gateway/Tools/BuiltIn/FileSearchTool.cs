using System.Text;
using System.Text.Json;
using LeanKernel.Gateway.Configuration;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Gateway.Tools.BuiltIn;

/// <summary>
/// Provides the LeanKernel-owned <c>file_search</c> built-in tool.
/// Searches local files within the <c>Files:RootPath</c> boundary.
/// </summary>
public static class FileSearchTool
{
    private const string ToolName = "file_search";
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;
    private const int DefaultMaxDepth = 8;
    private const int DefaultMaxScanned = 5_000;
    private const long DefaultMaxFileBytes = 1_000_000;

    /// <summary>
    /// Creates the file_search tool definition.
    /// </summary>
    /// <param name="scopeFactory">Factory used to create a DI scope per invocation.</param>
    /// <returns>The configured tool definition.</returns>
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Search local data files by filename/path or text content within the allowed data directory",
            Category = "filesystem",
            Parameters =
            [
                new ToolParameter { Name = "query",   Type = "string",  Description = "Filename or content search term (use when name or content is unspecified)", Required = false },
                new ToolParameter { Name = "name",    Type = "string",  Description = "File name or partial name to search", Required = false },
                new ToolParameter { Name = "content", Type = "string",  Description = "Text content to search within files", Required = false },
                new ToolParameter { Name = "path",    Type = "string",  Description = "Sub-path within the allowed root to search", Required = false },
                new ToolParameter { Name = "limit",   Type = "integer", Description = $"Maximum results (default {DefaultLimit}, max {MaxLimit})", Required = false },
                new ToolParameter { Name = "maxDepth", Type = "integer", Description = "Maximum directory recursion depth", Required = false }
            ],
            Handler = async (args, ct) =>
            {
                var query   = ToolArgumentReader.GetString(args, "query");
                var name    = ToolArgumentReader.GetString(args, "name");
                var content = ToolArgumentReader.GetString(args, "content");
                var path    = ToolArgumentReader.GetString(args, "path");
                var limit   = Math.Min(ToolArgumentReader.GetInt(args, "limit") ?? DefaultLimit, MaxLimit);
                var maxDepth = ToolArgumentReader.GetInt(args, "maxDepth") ?? DefaultMaxDepth;

                if (string.IsNullOrWhiteSpace(query) &&
                    string.IsNullOrWhiteSpace(name) &&
                    string.IsNullOrWhiteSpace(content))
                {
                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = false,
                        Error = "A query, name, or content search term is required."
                    };
                }

                using var scope = scopeFactory.CreateScope();
                var fileSettings = scope.ServiceProvider
                    .GetRequiredService<IOptions<FileSettings>>().Value;

                if (string.IsNullOrWhiteSpace(fileSettings.RootPath))
                {
                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = false,
                        Error = "Files:RootPath is not configured."
                    };
                }

                var startPath = FileSystemSupport.ResolveWithinRoot(fileSettings.RootPath, path);
                if (startPath is null)
                {
                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = false,
                        Error = "Access denied: path is outside the allowed directory."
                    };
                }

                if (!Directory.Exists(startPath))
                {
                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = false,
                        Error = $"Directory not found: {path}"
                    };
                }

                var results = await SearchAsync(
                    startPath,
                    fileSettings.RootPath,
                    query ?? name,
                    content,
                    limit,
                    maxDepth,
                    DefaultMaxScanned,
                    DefaultMaxFileBytes,
                    ct).ConfigureAwait(false);

                return new ToolResult
                {
                    ToolName = ToolName,
                    Success = true,
                    Output = JsonSerializer.Serialize(results)
                };
            }
        };
    }

    private static async Task<FileSearchResponse> SearchAsync(
        string startPath,
        string rootPath,
        string? nameTerm,
        string? contentTerm,
        int limit,
        int maxDepth,
        int maxScanned,
        long maxFileBytes,
        CancellationToken ct)
    {
        var matches = new List<FileSearchMatch>();
        var scanned = 0;
        var truncated = false;
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((startPath, 0));

        while (stack.Count > 0 && matches.Count < limit && !truncated)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = stack.Pop();

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(dir);
            }
            catch
            {
                continue;
            }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                if (scanned >= maxScanned)
                {
                    truncated = true;
                    break;
                }

                scanned++;

                if (Directory.Exists(entry))
                {
                    if (depth < maxDepth)
                    {
                        stack.Push((entry, depth + 1));
                    }

                    continue;
                }

                var fileName = Path.GetFileName(entry);
                var relativePath = Path.GetRelativePath(rootPath, entry);
                var nameMatch = !string.IsNullOrWhiteSpace(nameTerm) &&
                    fileName.Contains(nameTerm, StringComparison.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(contentTerm))
                {
                    var info = new FileInfo(entry);
                    if (info.Length <= maxFileBytes && IsTextFile(entry))
                    {
                        try
                        {
                            var text = await File.ReadAllTextAsync(entry, ct).ConfigureAwait(false);
                            if (text.Contains(contentTerm, StringComparison.OrdinalIgnoreCase))
                            {
                                matches.Add(new FileSearchMatch
                                {
                                    Path = relativePath,
                                    Name = fileName,
                                    MatchType = nameMatch ? "name+content" : "content",
                                    SizeBytes = info.Length
                                });
                                if (matches.Count >= limit)
                                {
                                    break;
                                }

                                continue;
                            }
                        }
                        catch
                        {
                            // skip unreadable files
                        }
                    }
                }

                if (nameMatch)
                {
                    matches.Add(new FileSearchMatch
                    {
                        Path = relativePath,
                        Name = fileName,
                        MatchType = "name",
                        SizeBytes = new FileInfo(entry).Length
                    });

                    if (matches.Count >= limit)
                    {
                        break;
                    }
                }
            }
        }

        return new FileSearchResponse
        {
            Matches = matches,
            Scanned = scanned,
            Truncated = truncated
        };
    }

    private static bool IsTextFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".txt" or ".md" or ".json" or ".xml" or ".csv" or ".yaml" or ".yml"
            or ".log" or ".cs" or ".ts" or ".js" or ".py" or ".sh" or ".html" or ".htm"
            or ".css" or ".sql" or ".toml" or ".ini" or ".cfg" or ".conf" or ".env" or "";
    }

    private sealed class FileSearchResponse
    {
        public List<FileSearchMatch> Matches { get; set; } = [];
        public int Scanned { get; set; }
        public bool Truncated { get; set; }
    }

    private sealed class FileSearchMatch
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string MatchType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
    }
}
