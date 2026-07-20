using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.BuiltIn.FileSystem;

/// <summary>
/// Deletes a file or directory within the allowed data directory.
/// </summary>
public static class FileDeleteTool
{
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "file_delete",
            Description = "Delete a file or directory within the allowed data directory",
            Category = "filesystem",
            Parameters =
            [
                new ToolParameter { Name = "path", Type = "string", Description = "Relative path", Required = true },
                new ToolParameter { Name = "recursive", Type = "boolean", Description = "Delete directories recursively", Required = false },
                new ToolParameter { Name = "missingOk", Type = "boolean", Description = "Succeed when path is missing", Required = false }
            ],
            Handler = (args, ct) =>
            {
                var path = ToolArgumentReader.GetString(args, "path");
                var recursive = ToolArgumentReader.GetBoolOrDefault(args, "recursive", false);
                var missingOk = ToolArgumentReader.GetBoolOrDefault(args, "missingOk", true);

                if (string.IsNullOrWhiteSpace(path))
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_delete", Success = false, Error = "Path is required" });
                }

                using var scope = scopeFactory.CreateScope();
                var fileSettings = scope.ServiceProvider.GetRequiredService<IOptions<FileSettings>>().Value;
                var fullPath = FileSystemSupport.ResolveWithinRoot(fileSettings.RootPath, path);
                if (fullPath is null)
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_delete", Success = false, Error = "Access denied: path is outside the allowed directory" });
                }

                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    return Task.FromResult(new ToolResult { ToolName = "file_delete", Success = true, Output = $"Deleted file {path}" });
                }

                if (Directory.Exists(fullPath))
                {
                    if (!recursive)
                    {
                        return Task.FromResult(new ToolResult { ToolName = "file_delete", Success = false, Error = "Directory delete requires recursive=true" });
                    }

                    Directory.Delete(fullPath, recursive: true);
                    return Task.FromResult(new ToolResult { ToolName = "file_delete", Success = true, Output = $"Deleted directory {path}" });
                }

                return Task.FromResult(missingOk
                    ? new ToolResult { ToolName = "file_delete", Success = true, Output = $"Path not found; nothing deleted: {path}" }
                    : new ToolResult { ToolName = "file_delete", Success = false, Error = $"Path not found: {path}" });
            }
        };
    }
}