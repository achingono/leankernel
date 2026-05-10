using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Represents the file system touch tool.
/// </summary>
[ToolMetadata(
    Name = "file_touch",
    Description = "Create an empty file or update its timestamp in approved paths.",
    Category = ToolCategory.FileSystem)]
public sealed class FileSystemTouchTool : ITool
{
    private readonly string _allowedBasePath;
    private readonly HashSet<string> _writeAllowlist;

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name => "file_touch";
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description => "Create an empty file or update its modification time in approved paths.";
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
            "path": { "type": "string", "description": "Relative file path within the data directory" },
            "createDirectories": { "type": "boolean", "default": true }
          },
          "required": ["path"]
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemTouchTool" /> class.
    /// </summary>
    /// <param name="allowedBasePath">The allowed base path.</param>
    /// <param name="writeAllowlist">The write allowlist.</param>
    public FileSystemTouchTool(string allowedBasePath = "/app/data", IEnumerable<string>? writeAllowlist = null)
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
    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            var relativePath = doc.RootElement.GetProperty("path").GetString() ?? string.Empty;
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
                    Error = $"Touch denied: '{relativePath}' is not in the default write policy allowlist",
                    Duration = sw.Elapsed
                };
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (createDirectories && !string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            if (!File.Exists(fullPath))
            {
                await File.WriteAllTextAsync(fullPath, string.Empty, ct);
            }

            File.SetLastWriteTimeUtc(fullPath, DateTime.UtcNow);

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = $"Touched {relativePath}",
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
