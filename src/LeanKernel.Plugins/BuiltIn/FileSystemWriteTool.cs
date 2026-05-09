using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

[ToolMetadata(
    Name = "file_write",
    Description = "Write or append file content in approved paths within the data directory.",
    Category = ToolCategory.FileSystem)]
public sealed class FileSystemWriteTool : ITool
{
    private readonly string _allowedBasePath;
    private readonly HashSet<string> _writeAllowlist;

    public string Name => "file_write";
    public string Description => "Write or append to a local file in approved paths.";
    public string Category => ToolCategory.FileSystem.ToString().ToLower();
    public string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Relative path within the data directory" },
            "content": { "type": "string", "description": "Content to write" },
            "append": { "type": "boolean", "default": false },
            "createDirectories": { "type": "boolean", "default": true }
          },
          "required": ["path", "content"]
        }
        """;

    public FileSystemWriteTool(string allowedBasePath = "/app/data", IEnumerable<string>? writeAllowlist = null)
    {
        _allowedBasePath = Path.GetFullPath(allowedBasePath);
        _writeAllowlist = new HashSet<string>(
            (writeAllowlist ?? FileSystemPolicy.DefaultWriteAllowlist).Select(FileSystemPolicy.NormalizeRelativePath),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            var relativePath = doc.RootElement.GetProperty("path").GetString() ?? string.Empty;
            var content = doc.RootElement.GetProperty("content").GetString() ?? string.Empty;
            var append = doc.RootElement.TryGetProperty("append", out var appendEl) && appendEl.GetBoolean();
            var createDirectories = !doc.RootElement.TryGetProperty("createDirectories", out var cdEl) || cdEl.GetBoolean();

            var fullPath = FileSystemPolicy.ResolveWithinBase(_allowedBasePath, relativePath);
            if (fullPath is null)
            {
                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = "Access denied: path is outside the allowed directory",
                    Duration = sw.Elapsed
                };
            }

            if (!FileSystemPolicy.IsWriteAllowed(_allowedBasePath, fullPath, _writeAllowlist))
            {
                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = $"Write denied: '{relativePath}' is not in the default write policy allowlist",
                    Duration = sw.Elapsed
                };
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && createDirectories)
                Directory.CreateDirectory(directory);

            if (append)
                await File.AppendAllTextAsync(fullPath, content, ct);
            else
                await File.WriteAllTextAsync(fullPath, content, ct);

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = $"Wrote {content.Length} characters to {relativePath}",
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