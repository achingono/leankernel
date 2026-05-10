using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Represents the file system delete tool.
/// </summary>
[ToolMetadata(
    Name = "file_delete",
    Description = "Delete files or directories in approved paths.",
    Category = ToolCategory.FileSystem)]
public sealed class FileSystemDeleteTool : ITool
{
    private readonly string _allowedBasePath;
    private readonly HashSet<string> _writeAllowlist;

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name => "file_delete";
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description => "Delete a file or directory in approved paths.";
    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public string Category => ToolCategory.FileSystem.ToString().ToLower();
    /// <summary>
    /// Gets or sets the parameters schema.
    /// </summary>
    public string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Relative path within the data directory" },
            "recursive": { "type": "boolean", "default": false },
            "missingOk": { "type": "boolean", "default": true }
          },
          "required": ["path"]
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemDeleteTool" /> class.
    /// </summary>
    /// <param name="allowedBasePath">The allowed base path.</param>
    /// <param name="writeAllowlist">The write allowlist.</param>
    public FileSystemDeleteTool(string allowedBasePath = "/app/data", IEnumerable<string>? writeAllowlist = null)
    {
        _allowedBasePath = Path.GetFullPath(allowedBasePath);
        _writeAllowlist = new HashSet<string>(
            (writeAllowlist ?? FileSystemPolicy.DefaultWriteAllowlist).Select(FileSystemPolicy.NormalizeRelativePath),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Executes the execute async operation.
    /// </summary>
    /// <param name="parametersJson">The parameters json.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            var relativePath = doc.RootElement.GetProperty("path").GetString() ?? string.Empty;
            var recursive = doc.RootElement.TryGetProperty("recursive", out var rEl) && rEl.GetBoolean();
            var missingOk = !doc.RootElement.TryGetProperty("missingOk", out var mEl) || mEl.GetBoolean();

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

            if (!FileSystemPolicy.IsWriteAllowed(_allowedBasePath, fullPath, _writeAllowlist))
            {
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = $"Delete denied: '{relativePath}' is not in the default write policy allowlist",
                    Duration = sw.Elapsed
                });
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = true,
                    Output = $"Deleted file {relativePath}",
                    Duration = sw.Elapsed
                });
            }

            if (Directory.Exists(fullPath))
            {
                if (!recursive)
                {
                    return Task.FromResult(new ToolResult
                    {
                        ToolName = Name,
                        Success = false,
                        Error = "Directory delete requires recursive=true",
                        Duration = sw.Elapsed
                    });
                }

                Directory.Delete(fullPath, recursive: true);
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = true,
                    Output = $"Deleted directory {relativePath}",
                    Duration = sw.Elapsed
                });
            }

            if (missingOk)
            {
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = true,
                    Output = $"Path not found; nothing deleted: {relativePath}",
                    Duration = sw.Elapsed
                });
            }

            return Task.FromResult(new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = $"Path not found: {relativePath}",
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
}
