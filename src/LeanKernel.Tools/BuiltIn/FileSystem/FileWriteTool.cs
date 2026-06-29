using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools.BuiltIn.FileSystem;

public static class FileWriteTool
{
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "file_write",
            Description = "Write a local file under /app/data. For user-facing documents (notes, guides, group docs), use the documents/ subdirectory. Use managed-documents/ for UI-uploaded files.",
            Category = "filesystem",
            Parameters =
            [
                new ToolParameter { Name = "path", Type = "string", Description = "Relative file path", Required = true },
                new ToolParameter { Name = "content", Type = "string", Description = "File content", Required = true },
                new ToolParameter { Name = "append", Type = "boolean", Description = "Append instead of overwrite", Required = false },
                new ToolParameter { Name = "createDirectories", Type = "boolean", Description = "Create parent directories", Required = false }
            ],
            Handler = async (args, ct) =>
            {
                var path = ToolArgumentReader.GetString(args, "path");
                var content = ToolArgumentReader.GetString(args, "content");
                var append = ToolArgumentReader.GetBoolOrDefault(args, "append", false);
                var createDirectories = ToolArgumentReader.GetBoolOrDefault(args, "createDirectories", true);

                if (string.IsNullOrWhiteSpace(path))
                {
                    return new ToolResult { ToolName = "file_write", Success = false, Error = "Path is required" };
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    return new ToolResult { ToolName = "file_write", Success = false, Error = "Content is required" };
                }

                using var scope = scopeFactory.CreateScope();
                var config = scope.ServiceProvider.GetRequiredService<IOptions<LeanKernelConfig>>().Value.FileSystem;
                var fullPath = FileSystemSupport.ResolveWithinRoot(config.AllowedRoot, path);
                if (fullPath is null)
                {
                    return new ToolResult { ToolName = "file_write", Success = false, Error = "Access denied: path is outside the allowed directory" };
                }

                var directory = Path.GetDirectoryName(fullPath);
                if (createDirectories && !string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (append)
                {
                    await File.AppendAllTextAsync(fullPath, content, ct);
                }
                else
                {
                    await File.WriteAllTextAsync(fullPath, content, ct);
                }

                return new ToolResult
                {
                    ToolName = "file_write",
                    Success = true,
                    Output = $"Wrote {content.Length} characters to {path}"
                };
            }
        };
    }
}
