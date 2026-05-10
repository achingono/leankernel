using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Represents the file system stat tool.
/// </summary>
[ToolMetadata(
    Name = "file_stat",
    Description = "Get file or directory metadata within the allowed data directory.",
    Category = ToolCategory.FileSystem)]
public sealed class FileSystemStatTool : ITool
{
    private readonly string _allowedBasePath;

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name => "file_stat";
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description => "Return metadata for a file or directory within the data directory.";
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
            "path": { "type": "string", "description": "Relative path within the data directory" }
          },
          "required": ["path"]
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemStatTool" /> class.
    /// </summary>
    /// <param name="allowedBasePath">The allowed base path.</param>
    public FileSystemStatTool(string allowedBasePath = "/app/data")
    {
        _allowedBasePath = Path.GetFullPath(allowedBasePath);
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

            if (File.Exists(fullPath))
            {
                var info = new FileInfo(fullPath);
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = true,
                    Output = JsonSerializer.Serialize(new
                    {
                        path = relativePath,
                        kind = "file",
                        sizeBytes = info.Length,
                        createdUtc = info.CreationTimeUtc,
                        modifiedUtc = info.LastWriteTimeUtc,
                        isReadOnly = info.IsReadOnly
                    }),
                    Duration = sw.Elapsed
                });
            }

            if (Directory.Exists(fullPath))
            {
                var info = new DirectoryInfo(fullPath);
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = true,
                    Output = JsonSerializer.Serialize(new
                    {
                        path = relativePath,
                        kind = "directory",
                        createdUtc = info.CreationTimeUtc,
                        modifiedUtc = info.LastWriteTimeUtc
                    }),
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
