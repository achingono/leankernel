using System.Diagnostics.CodeAnalysis;

using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.BuiltIn.FileSystem;

/// <summary>
/// Copies a file or directory within the allowed data directory.
/// </summary>
public static class FileCopyTool
{
    [SuppressMessage("Major Code Smell", "S3776", Justification = "Tool handler stays explicit for path safety checks.")]
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "file_copy",
            Description = "Copy a file or directory within the allowed data directory",
            Category = "filesystem",
            Parameters =
            [
                new ToolParameter { Name = "sourcePath", Type = "string", Description = "Source relative path", Required = true },
                new ToolParameter { Name = "destinationPath", Type = "string", Description = "Destination relative path", Required = true },
                new ToolParameter { Name = "overwrite", Type = "boolean", Description = "Overwrite existing destination", Required = false },
                new ToolParameter { Name = "recursive", Type = "boolean", Description = "Copy directories recursively", Required = false },
                new ToolParameter { Name = "createDirectories", Type = "boolean", Description = "Create parent directories", Required = false }
            ],
            Handler = (args, ct) =>
            {
                var sourcePath = ToolArgumentReader.GetString(args, "sourcePath");
                var destinationPath = ToolArgumentReader.GetString(args, "destinationPath");
                var overwrite = ToolArgumentReader.GetBoolOrDefault(args, "overwrite", false);
                var recursive = ToolArgumentReader.GetBoolOrDefault(args, "recursive", true);
                var createDirectories = ToolArgumentReader.GetBoolOrDefault(args, "createDirectories", true);

                if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_copy", Success = false, Error = "Source and destination paths are required" });
                }

                using var scope = scopeFactory.CreateScope();
                var fileSettings = scope.ServiceProvider.GetRequiredService<IOptions<FileSettings>>().Value;
                var sourceFullPath = FileSystemSupport.ResolveWithinRoot(fileSettings.RootPath, sourcePath);
                var destinationFullPath = FileSystemSupport.ResolveWithinRoot(fileSettings.RootPath, destinationPath);
                if (sourceFullPath is null || destinationFullPath is null)
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_copy", Success = false, Error = "Access denied: source or destination path is outside the allowed directory" });
                }

                if (PathsEqual(sourceFullPath, destinationFullPath))
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_copy", Success = false, Error = "Source and destination must be different paths" });
                }

                if (File.Exists(sourceFullPath))
                {
                    var destinationDirectory = Path.GetDirectoryName(destinationFullPath);
                    if (createDirectories && !string.IsNullOrEmpty(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    File.Copy(sourceFullPath, destinationFullPath, overwrite);
                    return Task.FromResult(new ToolResult { ToolName = "file_copy", Success = true, Output = $"Copied {sourcePath} to {destinationPath}" });
                }

                if (!Directory.Exists(sourceFullPath))
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_copy", Success = false, Error = $"Source path not found: {sourcePath}" });
                }

                if (!recursive)
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_copy", Success = false, Error = "Directory copy requires recursive=true" });
                }

                if (IsSameOrDescendantPath(sourceFullPath, destinationFullPath))
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_copy", Success = false, Error = "Destination directory cannot be the source directory or a child of it" });
                }

                CopyDirectory(sourceFullPath, destinationFullPath, overwrite, createDirectories);
                return Task.FromResult(new ToolResult { ToolName = "file_copy", Success = true, Output = $"Copied {sourcePath} to {destinationPath}" });
            }
        };
    }

    private static bool PathsEqual(string leftPath, string rightPath)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(NormalizePath(leftPath), NormalizePath(rightPath), comparison);
    }

    private static bool IsSameOrDescendantPath(string sourcePath, string destinationPath)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedSource = NormalizePath(sourcePath);
        var normalizedDestination = NormalizePath(destinationPath);

        return normalizedDestination.Equals(normalizedSource, comparison)
            || normalizedDestination.StartsWith(normalizedSource + Path.DirectorySeparatorChar, comparison);
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void CopyDirectory(string sourceDir, string destinationDir, bool overwrite, bool createDirectories)
    {
        if (createDirectories)
        {
            Directory.CreateDirectory(destinationDir);
        }

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            File.Copy(file, Path.Combine(destinationDir, fileName), overwrite);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            CopyDirectory(dir, Path.Combine(destinationDir, dirName), overwrite, createDirectories);
        }
    }
}