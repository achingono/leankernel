using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.BuiltIn.FileSystem;

/// <summary>
/// Extracts readable text from a file using the text extraction pipeline.
/// </summary>
public static class ExtractTextTool
{
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "extract_text",
            Description = "Extract readable text from a file in the allowed data directory",
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
                    return new ToolResult { ToolName = "extract_text", Success = false, Error = "Path is required" };
                }

                using var scope = scopeFactory.CreateScope();
                var fileSettings = scope.ServiceProvider.GetRequiredService<IOptions<FileSettings>>().Value;
                var fullPath = FileSystemSupport.ResolveWithinRoot(fileSettings.RootPath, path);
                if (fullPath is null)
                {
                    return new ToolResult { ToolName = "extract_text", Success = false, Error = "Access denied: path is outside the allowed directory" };
                }

                if (!File.Exists(fullPath))
                {
                    return new ToolResult { ToolName = "extract_text", Success = false, Error = $"File not found: {path}" };
                }

                var output = await TextExtractionHelper.ExtractAsync(fullPath, fileSettings.ScratchRoot, fileSettings.PythonExecutable, fileSettings.MaxExtractedCharacters, ct);
                return new ToolResult { ToolName = "extract_text", Success = true, Output = output };
            }
        };
    }
}