using System.Diagnostics.CodeAnalysis;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.BuiltIn.FileSystem;

/// <summary>
/// Moves or renames a file or directory within the allowed data directory.
/// </summary>
public static class FileMoveTool
{
    [SuppressMessage("Major Code Smell", "S3776", Justification = "Tool handler stays explicit for path safety checks.")]
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "file_move",
            Description = "Move or rename a file or directory within the allowed data directory",
            Category = "filesystem",
            Parameters =
            [
                new ToolParameter { Name = "sourcePath", Type = "string", Description = "Source relative path", Required = true },
                new ToolParameter { Name = "destinationPath", Type = "string", Description = "Destination relative path", Required = true },
                new ToolParameter { Name = "overwrite", Type = "boolean", Description = "Overwrite existing destination", Required = false },
                new ToolParameter { Name = "createDirectories", Type = "boolean", Description = "Create parent directories", Required = false }
            ],
            Handler = (args, ct) =>
            {
                var sourcePath = ToolArgumentReader.GetString(args, "sourcePath");
                var destinationPath = ToolArgumentReader.GetString(args, "destinationPath");
                var overwrite = ToolArgumentReader.GetBoolOrDefault(args, "overwrite", false);
                var createDirectories = ToolArgumentReader.GetBoolOrDefault(args, "createDirectories", true);

                if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_move", Success = false, Error = "Source and destination paths are required" });
                }

                using var scope = scopeFactory.CreateScope();
                var fileSettings = scope.ServiceProvider.GetRequiredService<IOptions<FileSettings>>().Value;
                var sourceFullPath = FileSystemSupport.ResolveWithinRoot(fileSettings.RootPath, sourcePath);
                var destinationFullPath = FileSystemSupport.ResolveWithinRoot(fileSettings.RootPath, destinationPath);
                if (sourceFullPath is null || destinationFullPath is null)
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_move", Success = false, Error = "Access denied: source or destination path is outside the allowed directory" });
                }

                if (File.Exists(sourceFullPath))
                {
                    var destinationDirectory = Path.GetDirectoryName(destinationFullPath);
                    if (createDirectories && !string.IsNullOrEmpty(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    File.Move(sourceFullPath, destinationFullPath, overwrite);
                    return Task.FromResult(new ToolResult { ToolName = "file_move", Success = true, Output = $"Moved {sourcePath} to {destinationPath}" });
                }

                if (!Directory.Exists(sourceFullPath))
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_move", Success = false, Error = $"Source path not found: {sourcePath}" });
                }

                if (Directory.Exists(destinationFullPath))
                {
                    if (!overwrite)
                    {
                        return Task.FromResult(new ToolResult { ToolName = "file_move", Success = false, Error = $"Destination directory already exists: {destinationPath}" });
                    }

                    Directory.Delete(destinationFullPath, recursive: true);
                }

                if (createDirectories)
                {
                    var destinationDirectory = Path.GetDirectoryName(destinationFullPath);
                    if (!string.IsNullOrEmpty(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }
                }

                Directory.Move(sourceFullPath, destinationFullPath);
                return Task.FromResult(new ToolResult { ToolName = "file_move", Success = true, Output = $"Moved {sourcePath} to {destinationPath}" });
            }
        };
    }
}
