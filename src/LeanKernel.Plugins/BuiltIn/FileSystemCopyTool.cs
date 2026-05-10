using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Represents the file system copy tool.
/// </summary>
[ToolMetadata(
    Name = "file_copy",
    Description = "Copy files/directories into approved paths.",
    Category = ToolCategory.FileSystem)]
public sealed class FileSystemCopyTool : ITool
{
    private readonly string _allowedBasePath;
    private readonly HashSet<string> _writeAllowlist;

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name => "file_copy";
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description => "Copy a file or directory into approved paths.";
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
            "sourcePath": { "type": "string" },
            "destinationPath": { "type": "string" },
            "overwrite": { "type": "boolean", "default": false },
            "recursive": { "type": "boolean", "default": true },
            "createDirectories": { "type": "boolean", "default": true }
          },
          "required": ["sourcePath", "destinationPath"]
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemCopyTool" /> class.
    /// </summary>
    /// <param name="allowedBasePath">The allowed base path.</param>
    /// <param name="writeAllowlist">The write allowlist.</param>
    public FileSystemCopyTool(string allowedBasePath = "/app/data", IEnumerable<string>? writeAllowlist = null)
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
            var sourcePath = doc.RootElement.GetProperty("sourcePath").GetString() ?? string.Empty;
            var destinationPath = doc.RootElement.GetProperty("destinationPath").GetString() ?? string.Empty;
            var overwrite = doc.RootElement.TryGetProperty("overwrite", out var owEl) && owEl.GetBoolean();
            var recursive = !doc.RootElement.TryGetProperty("recursive", out var rcEl) || rcEl.GetBoolean();
            var createDirectories = !doc.RootElement.TryGetProperty("createDirectories", out var cdEl) || cdEl.GetBoolean();

            var sourceFullPath = FileSystemPolicy.ResolveWithinBase(_allowedBasePath, sourcePath);
            var destinationFullPath = FileSystemPolicy.ResolveWithinBase(_allowedBasePath, destinationPath);
            if (sourceFullPath is null || destinationFullPath is null)
            {
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = "Access denied: source or destination path is outside the allowed directory",
                    Duration = sw.Elapsed
                });
            }

            if (!FileSystemPolicy.IsWriteAllowed(_allowedBasePath, destinationFullPath, _writeAllowlist))
            {
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = "Copy denied: destination is not in the default write policy allowlist",
                    Duration = sw.Elapsed
                });
            }

            var destinationDirectory = Path.GetDirectoryName(destinationFullPath);
            if (createDirectories && !string.IsNullOrEmpty(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            if (File.Exists(sourceFullPath))
            {
                File.Copy(sourceFullPath, destinationFullPath, overwrite);
            }
            else if (Directory.Exists(sourceFullPath))
            {
                if (!recursive)
                {
                    return Task.FromResult(new ToolResult
                    {
                        ToolName = Name,
                        Success = false,
                        Error = "Directory copy requires recursive=true",
                        Duration = sw.Elapsed
                    });
                }

                CopyDirectory(sourceFullPath, destinationFullPath, overwrite);
            }
            else
            {
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = $"Source path not found: {sourcePath}",
                    Duration = sw.Elapsed
                });
            }

            return Task.FromResult(new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = $"Copied {sourcePath} to {destinationPath}",
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

    private static void CopyDirectory(string sourceDir, string destinationDir, bool overwrite)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destinationFile = Path.Combine(destinationDir, fileName);
            File.Copy(file, destinationFile, overwrite);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            var destinationSubDir = Path.Combine(destinationDir, dirName);
            CopyDirectory(dir, destinationSubDir, overwrite);
        }
    }
}
