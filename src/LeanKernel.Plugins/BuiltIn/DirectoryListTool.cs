using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

[ToolMetadata(
    Name = "directory_list",
    Description = "List files and directories within the allowed data directory.",
    Category = ToolCategory.FileSystem)]
public sealed class DirectoryListTool : ITool
{
    private readonly string _allowedBasePath;

    public string Name => "directory_list";
    public string Description => "List directory entries within the data directory.";
    public string Category => ToolCategory.FileSystem.ToString().ToLower();
    public string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Relative directory path within the data directory", "default": "" },
            "includeHidden": { "type": "boolean", "default": false }
          }
        }
        """;

    public DirectoryListTool(string allowedBasePath = "/app/data")
    {
        _allowedBasePath = Path.GetFullPath(allowedBasePath);
    }

    public Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            var relativePath = doc.RootElement.TryGetProperty("path", out var pathEl)
                ? pathEl.GetString() ?? string.Empty
                : string.Empty;
            var includeHidden = doc.RootElement.TryGetProperty("includeHidden", out var hiddenEl) && hiddenEl.GetBoolean();

            var fullPath = FileSystemPolicy.ResolveWithinBase(_allowedBasePath, relativePath);
            if (fullPath is null)
            {
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = "Access denied: path is outside the allowed directory",
                    Duration = sw.Elapsed
                });
            }

            if (!Directory.Exists(fullPath))
            {
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = $"Directory not found: {relativePath}",
                    Duration = sw.Elapsed
                });
            }

            var entries = Directory.EnumerateFileSystemEntries(fullPath)
                .Select(path => new FileSystemEntryInfo(
                    Name: Path.GetFileName(path),
                    Path: Path.GetRelativePath(_allowedBasePath, path).Replace('\\', '/'),
                    Kind: Directory.Exists(path) ? "directory" : "file",
                    SizeBytes: Directory.Exists(path) ? null : new FileInfo(path).Length))
                .Where(entry => includeHidden || !entry.Name.StartsWith(".", StringComparison.Ordinal))
                .OrderBy(entry => entry.Kind)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Task.FromResult(new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = JsonSerializer.Serialize(entries),
                Duration = sw.Elapsed
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = ex.Message,
                Duration = sw.Elapsed
            });
        }
    }

    private sealed record FileSystemEntryInfo(string Name, string Path, string Kind, long? SizeBytes);
}