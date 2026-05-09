using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Safe local file operations. Limited to allowed directories
/// to prevent unauthorized access.
/// </summary>
[ToolMetadata(
    Name = "file_read",
    Description = "Read contents of a local file within the allowed data directory.",
    Category = ToolCategory.FileSystem)]
public class FileSystemReadTool : ITool
{
    private readonly string _allowedBasePath;

    public string Name => "file_read";
    public string Description => "Read a local file from the data directory.";
    public string Category => ToolCategory.FileSystem.ToString().ToLower();
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Relative path within the data directory" },
            "maxLines": { "type": "integer", "default": 100 }
          },
          "required": ["path"]
        }
        """;

    public FileSystemReadTool(string allowedBasePath = "/app/data")
    {
        _allowedBasePath = Path.GetFullPath(allowedBasePath);
    }

    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var doc = JsonDocument.Parse(parametersJson);
            var relativePath = doc.RootElement.GetProperty("path").GetString()!;
            var maxLines = doc.RootElement.TryGetProperty("maxLines", out var ml) ? ml.GetInt32() : 100;

            var fullPath = Path.GetFullPath(Path.Combine(_allowedBasePath, relativePath));

            // Security: ensure path is within allowed directory
            if (!fullPath.StartsWith(_allowedBasePath, StringComparison.OrdinalIgnoreCase))
            {
                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = "Access denied: path is outside the allowed directory",
                    Duration = sw.Elapsed
                };
            }

            if (!File.Exists(fullPath))
            {
                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = $"File not found: {relativePath}",
                    Duration = sw.Elapsed
                };
            }

            var lines = await File.ReadAllLinesAsync(fullPath, ct);
            var content = string.Join("\n", lines.Take(maxLines));
            if (lines.Length > maxLines)
                content += $"\n... ({lines.Length - maxLines} more lines)";

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = content,
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
}

[Obsolete("Use FileSystemReadTool instead.")]
public sealed class FileSystemTool : FileSystemReadTool
{
    public FileSystemTool(string allowedBasePath = "/app/data")
        : base(allowedBasePath)
    {
    }
}
