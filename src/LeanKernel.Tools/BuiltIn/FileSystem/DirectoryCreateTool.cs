using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools.BuiltIn.FileSystem;

/// <summary>
/// Provides functionality for directory create tool.
/// </summary>
public static class DirectoryCreateTool
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
                var config = scope.ServiceProvider.GetRequiredService<IOptions<LeanKernelConfig>>().Value.FileSystem;
                var fullPath = FileSystemSupport.ResolveWithinRoot(config.AllowedRoot, path);
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
