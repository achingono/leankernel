using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools.BuiltIn.FileSystem;

/// <summary>
/// Provides functionality for file touch tool.
/// </summary>
public static class FileTouchTool
{
    /// <summary>
    /// Executes create.
    /// </summary>
    /// <param name="scopeFactory">The scope factory.</param>
    /// <returns>The operation result.</returns>
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "file_touch",
            Description = "Create an empty file or update its timestamp within the allowed data directory",
            Category = "filesystem",
            Parameters =
            [
                new ToolParameter { Name = "path", Type = "string", Description = "Relative file path", Required = true },
                new ToolParameter { Name = "createDirectories", Type = "boolean", Description = "Create parent directories", Required = false }
            ],
            Handler = async (args, ct) =>
            {
                var path = ToolArgumentReader.GetString(args, "path");
                var createDirectories = ToolArgumentReader.GetBoolOrDefault(args, "createDirectories", true);

                if (string.IsNullOrWhiteSpace(path))
                {
                    return new ToolResult { ToolName = "file_touch", Success = false, Error = "Path is required" };
                }

                using var scope = scopeFactory.CreateScope();
                var config = scope.ServiceProvider.GetRequiredService<IOptions<LeanKernelConfig>>().Value.FileSystem;
                var fullPath = FileSystemSupport.ResolveWithinRoot(config.AllowedRoot, path);
                if (fullPath is null)
                {
                    return new ToolResult { ToolName = "file_touch", Success = false, Error = "Access denied: path is outside the allowed directory" };
                }

                var directory = Path.GetDirectoryName(fullPath);
                if (createDirectories && !string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(fullPath))
                {
                    await File.WriteAllTextAsync(fullPath, string.Empty, ct);
                }

                File.SetLastWriteTimeUtc(fullPath, DateTime.UtcNow);
                return new ToolResult { ToolName = "file_touch", Success = true, Output = $"Touched {path}" };
            }
        };
    }
}
