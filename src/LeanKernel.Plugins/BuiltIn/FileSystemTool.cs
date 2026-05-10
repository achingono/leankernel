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

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name => "file_read";
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description => "Read a local file from the data directory.";
    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public string Category => ToolCategory.FileSystem.ToString().ToLower();
    /// <summary>
    /// Gets or sets the parameters schema.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemReadTool" /> class.
    /// </summary>
    /// <param name="allowedBasePath">The allowed base path.</param>
    /// <returns>The operation result.</returns>
    public FileSystemReadTool(string allowedBasePath = "/app/data")
    {
        _allowedBasePath = Path.GetFullPath(allowedBasePath);
    }

    /// <summary>
    /// Executes the execute async operation.
    /// </summary>
    /// <param name="parametersJson">The parameters json.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
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

/// <summary>
/// Represents the file system tool.
/// </summary>
[Obsolete("Use FileSystemReadTool instead.")]
public sealed class FileSystemTool : FileSystemReadTool
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemTool" /> class.
    /// </summary>
    /// <param name="allowedBasePath">The allowed base path.</param>
    public FileSystemTool(string allowedBasePath = "/app/data")
        : base(allowedBasePath)
    {
    }
}
