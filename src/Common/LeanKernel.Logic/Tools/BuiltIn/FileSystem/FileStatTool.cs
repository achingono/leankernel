using System.Text.Json;

using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.BuiltIn.FileSystem;

/// <summary>
/// Gets file or directory metadata.
/// </summary>
public static class FileStatTool
{
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "file_stat",
            Description = "Get file or directory metadata within the allowed data directory",
            Category = "filesystem",
            Parameters =
            [
                new ToolParameter { Name = "path", Type = "string", Description = "Relative path", Required = true }
            ],
            Handler = (args, ct) =>
            {
                var path = ToolArgumentReader.GetString(args, "path");
                if (string.IsNullOrWhiteSpace(path))
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_stat", Success = false, Error = "Path is required" });
                }

                using var scope = scopeFactory.CreateScope();
                var fileSettings = scope.ServiceProvider.GetRequiredService<IOptions<FileSettings>>().Value;
                var fullPath = FileSystemSupport.ResolveWithinRoot(fileSettings.RootPath, path);
                if (fullPath is null)
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_stat", Success = false, Error = "Access denied: path is outside the allowed directory" });
                }

                if (File.Exists(fullPath))
                {
                    var info = new FileInfo(fullPath);
                    return Task.FromResult(new ToolResult
                    {
                        ToolName = "file_stat",
                        Success = true,
                        Output = JsonSerializer.Serialize(new
                        {
                            path,
                            kind = "file",
                            sizeBytes = info.Length,
                            createdUtc = info.CreationTimeUtc,
                            modifiedUtc = info.LastWriteTimeUtc,
                            isReadOnly = info.IsReadOnly
                        })
                    });
                }

                if (Directory.Exists(fullPath))
                {
                    var info = new DirectoryInfo(fullPath);
                    return Task.FromResult(new ToolResult
                    {
                        ToolName = "file_stat",
                        Success = true,
                        Output = JsonSerializer.Serialize(new
                        {
                            path,
                            kind = "directory",
                            createdUtc = info.CreationTimeUtc,
                            modifiedUtc = info.LastWriteTimeUtc
                        })
                    });
                }

                return Task.FromResult(new ToolResult { ToolName = "file_stat", Success = false, Error = $"Path not found: {path}" });
            }
        };
    }
}