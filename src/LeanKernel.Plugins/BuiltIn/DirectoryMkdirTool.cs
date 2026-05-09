using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

[ToolMetadata(
    Name = "directory_mkdir",
    Description = "Create a directory within the allowed data directory.",
    Category = ToolCategory.FileSystem)]
public sealed class DirectoryMkdirTool : ITool
{
    private readonly string _allowedBasePath;
    private readonly HashSet<string> _writeAllowlist;

    public string Name => "directory_mkdir";
    public string Description => "Create a directory in approved paths.";
    public string Category => ToolCategory.FileSystem.ToString().ToLower();
    public string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Relative directory path within the data directory" }
          },
          "required": ["path"]
        }
        """;

    public DirectoryMkdirTool(string allowedBasePath = "/app/data", IEnumerable<string>? writeAllowlist = null)
    {
        _allowedBasePath = Path.GetFullPath(allowedBasePath);
        _writeAllowlist = new HashSet<string>(
            (writeAllowlist ?? FileSystemPolicy.DefaultWriteAllowlist).Select(FileSystemPolicy.NormalizeRelativePath),
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            var relativePath = doc.RootElement.GetProperty("path").GetString() ?? string.Empty;
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

            if (!IsDirectoryCreationAllowed(relativePath))
            {
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = $"Create directory denied: '{relativePath}' is not permitted by the default write policy",
                    Duration = sw.Elapsed
                });
            }

            Directory.CreateDirectory(fullPath);
            return Task.FromResult(new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = $"Created directory {relativePath}",
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

    private bool IsDirectoryCreationAllowed(string relativePath)
    {
        var normalized = FileSystemPolicy.NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalized))
            return false;

        foreach (var entry in _writeAllowlist)
        {
            if (entry.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}