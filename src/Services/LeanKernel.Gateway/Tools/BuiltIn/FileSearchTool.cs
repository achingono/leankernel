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
                    new FileSearchOptions(
                        startPath,
                        fileSettings.RootPath,
                        query ?? name,
                        content,
                        limit,
                        maxDepth,
                        DefaultMaxScanned,
                        DefaultMaxFileBytes),
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

    private sealed record FileSearchOptions(
        string StartPath,
        string RootPath,
        string? NameTerm,
        string? ContentTerm,
        int Limit,
        int MaxDepth,
        int MaxScanned,
        long MaxFileBytes);

    private sealed record FileEntryContext(
        string Entry,
        string RelativePath,
        string FileName,
        bool NameMatch);

    private static async Task<FileSearchResponse> SearchAsync(FileSearchOptions opts, CancellationToken ct)
    {
        var matches = new List<FileSearchMatch>();
        var scanned = 0;
        var truncated = false;
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((opts.StartPath, 0));

        while (stack.Count > 0 && matches.Count < opts.Limit && !truncated)
        {
            ct.ThrowIfCancellationRequested();
            (truncated, scanned) = await ProcessDirectoryAsync(matches, stack, opts, scanned, truncated, ct).ConfigureAwait(false);
        }

        return new FileSearchResponse { Matches = matches, Scanned = scanned, Truncated = truncated };
    }

    private static async Task<(bool truncated, int scanned)> ProcessDirectoryAsync(
        List<FileSearchMatch> matches,
        Stack<(string Path, int Depth)> stack,
        FileSearchOptions opts,
        int scanned,
        bool truncated,
        CancellationToken ct)
    {
        var (dir, depth) = stack.Pop();
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(dir);
        }
        catch
        {
            return (truncated, scanned);
        }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (scanned >= opts.MaxScanned)
            {
                return (true, scanned);
            }

            scanned++;
            if (Directory.Exists(entry))
            {
                if (depth < opts.MaxDepth)
                {
                    stack.Push((entry, depth + 1));
                }

                continue;
            }

            if (await TryAddMatchAsync(matches, entry, opts, ct).ConfigureAwait(false) && matches.Count >= opts.Limit)
            {
                break;
            }
        }

        return (truncated, scanned);
    }

    private static async Task<bool> TryAddMatchAsync(
        List<FileSearchMatch> matches,
        string entry,
        FileSearchOptions opts,
        CancellationToken ct)
    {
        var fileName = Path.GetFileName(entry);
        var relativePath = Path.GetRelativePath(opts.RootPath, entry);
        var nameMatch = !string.IsNullOrWhiteSpace(opts.NameTerm) &&
            fileName.Contains(opts.NameTerm, StringComparison.OrdinalIgnoreCase);
        var ctx = new FileEntryContext(entry, relativePath, fileName, nameMatch);

        var info = new FileInfo(entry);
        if (!string.IsNullOrWhiteSpace(opts.ContentTerm) && info.Length <= opts.MaxFileBytes && IsTextFile(entry) &&
            await TryAddContentMatchAsync(matches, ctx, info.Length, opts.ContentTerm, ct).ConfigureAwait(false))
        {
            return true;
        }

        if (!nameMatch)
        {
            return false;
        }

        matches.Add(new FileSearchMatch { Path = relativePath, Name = fileName, MatchType = "name", SizeBytes = new FileInfo(entry).Length });
        return true;
    }

    private static async Task<bool> TryAddContentMatchAsync(
        List<FileSearchMatch> matches,
        FileEntryContext ctx,
        long sizeBytes,
        string contentTerm,
        CancellationToken ct)
    {
        try
        {
            var text = await File.ReadAllTextAsync(ctx.Entry, ct).ConfigureAwait(false);
            if (!text.Contains(contentTerm, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            matches.Add(new FileSearchMatch
            {
                Path = ctx.RelativePath,
                Name = ctx.FileName,
                MatchType = ctx.NameMatch ? "name+content" : "content",
                SizeBytes = sizeBytes
            });

            return true;
        }
        catch
        {
            return false;
        }
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
