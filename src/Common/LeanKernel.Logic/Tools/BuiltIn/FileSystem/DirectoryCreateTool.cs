using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.BuiltIn.FileSystem;

/// <summary>
/// Creates a directory within the allowed data directory.
/// </summary>
public static class DirectoryCreateTool
{
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "directory_create",
            Description = "Create a directory within the allowed data directory",
            Category = "filesystem",
            Parameters =
            [
                new ToolParameter { Name = "path", Type = "string", Description = "Relative directory path", Required = true }
            ],
            Handler = (args, ct) =>
            {
                var path = ToolArgumentReader.GetString(args, "path");
                if (string.IsNullOrWhiteSpace(path))
                {
                    return Task.FromResult(new ToolResult { ToolName = "directory_create", Success = false, Error = "Path is required" });
                }

                using var scope = scopeFactory.CreateScope();
                var fileSettings = scope.ServiceProvider.GetRequiredService<IOptions<FileSettings>>().Value;
                var fullPath = FileSystemSupport.ResolveWithinRoot(fileSettings.RootPath, path);
                if (fullPath is null)
                {
                    return Task.FromResult(new ToolResult { ToolName = "directory_create", Success = false, Error = "Access denied: path is outside the allowed directory" });
                }

                Directory.CreateDirectory(fullPath);
                return Task.FromResult(new ToolResult { ToolName = "directory_create", Success = true, Output = $"Created directory {path}" });
            }
        };
    }
}
