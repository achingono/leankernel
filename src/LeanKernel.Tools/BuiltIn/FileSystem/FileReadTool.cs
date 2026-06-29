using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools.BuiltIn.FileSystem;

/// <summary>
/// Provides functionality for file read tool.
/// </summary>
public static class FileReadTool
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
            Name = "file_read",
            Description = "Read a local file from the allowed data directory",
            Category = "filesystem",
            Parameters =
            [
                new ToolParameter { Name = "path", Type = "string", Description = "Relative file path", Required = true }
            ],
            Handler = async (args, ct) =>
            {
                var path = ToolArgumentReader.GetString(args, "path");
                if (string.IsNullOrWhiteSpace(path))
                {
                    return new ToolResult { ToolName = "file_read", Success = false, Error = "Path is required" };
                }

                using var scope = scopeFactory.CreateScope();
                var config = scope.ServiceProvider.GetRequiredService<IOptions<LeanKernelConfig>>().Value.FileSystem;
                var fullPath = FileSystemSupport.ResolveWithinRoot(config.AllowedRoot, path);
                if (fullPath is null)
                {
                    return new ToolResult { ToolName = "file_read", Success = false, Error = "Access denied: path is outside the allowed directory" };
                }

                if (!File.Exists(fullPath))
                {
                    return new ToolResult { ToolName = "file_read", Success = false, Error = $"File not found: {path}" };
                }

                var output = await TextExtractionHelper.ExtractAsync(fullPath, config, ct);
                return new ToolResult { ToolName = "file_read", Success = true, Output = output };
            }
        };
    }
}
