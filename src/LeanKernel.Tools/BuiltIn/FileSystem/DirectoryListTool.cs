using System.Text.Json;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools.BuiltIn.FileSystem;

/// <summary>
/// Provides functionality for directory list tool.
/// </summary>
public static class DirectoryListTool
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
            Name = "directory_list",
            Description = "List files and directories within the allowed data directory",
            Category = "filesystem",
            Parameters =
            [
                new ToolParameter { Name = "path", Type = "string", Description = "Relative directory path", Required = false },
                new ToolParameter { Name = "includeHidden", Type = "boolean", Description = "Include hidden entries", Required = false }
            ],
            Handler = (args, ct) =>
            {
                var path = ToolArgumentReader.GetString(args, "path");
                var includeHidden = ToolArgumentReader.GetBoolOrDefault(args, "includeHidden", false);

                using var scope = scopeFactory.CreateScope();
                var config = scope.ServiceProvider.GetRequiredService<IOptions<LeanKernelConfig>>().Value.FileSystem;
                var fullPath = FileSystemSupport.ResolveWithinRoot(config.AllowedRoot, path);
                if (fullPath is null)
                {
                    return Task.FromResult(new ToolResult { ToolName = "directory_list", Success = false, Error = "Access denied: path is outside the allowed directory" });
                }

                if (!Directory.Exists(fullPath))
                {
                    return Task.FromResult(new ToolResult { ToolName = "directory_list", Success = false, Error = $"Directory not found: {path}" });
                }

                var entries = Directory.EnumerateFileSystemEntries(fullPath)
                    .Select(entry => new
                    {
                        name = Path.GetFileName(entry),
                        path = Path.GetRelativePath(config.AllowedRoot, entry).Replace('\\', '/'),
                        kind = Directory.Exists(entry) ? "directory" : "file",
                        sizeBytes = Directory.Exists(entry) ? (long?)null : new FileInfo(entry).Length
                    })
                    .Where(entry => includeHidden || !entry.name.StartsWith(".", StringComparison.Ordinal))
                    .OrderBy(entry => entry.kind)
                    .ThenBy(entry => entry.name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return Task.FromResult(new ToolResult { ToolName = "directory_list", Success = true, Output = JsonSerializer.Serialize(entries) });
            }
        };
    }
}
