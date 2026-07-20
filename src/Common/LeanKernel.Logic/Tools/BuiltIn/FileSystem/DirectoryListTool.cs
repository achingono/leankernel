using System.Text.Json;

using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.BuiltIn.FileSystem;

/// <summary>
/// Lists files and directories within the allowed data directory.
/// </summary>
public static class DirectoryListTool
{
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
                var fileSettings = scope.ServiceProvider.GetRequiredService<IOptions<FileSettings>>().Value;
                var fullPath = FileSystemSupport.ResolveWithinRoot(fileSettings.RootPath, path);
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
                        path = Path.GetRelativePath(fileSettings.RootPath, entry).Replace('\\', '/'),
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