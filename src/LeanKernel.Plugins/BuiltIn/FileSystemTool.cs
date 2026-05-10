using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Safe local file operations. Limited to allowed directories
/// to prevent unauthorized access.
/// </summary>
[ToolMetadata(
    Name = "file_read",
    Description = "Read contents of a local file within the allowed data directory.",
    Category = ToolCategory.FileSystem)]
public class FileSystemReadTool : ITool
{
    private readonly string _allowedBasePath;
    private readonly IAttachmentTextExtractionService? _textExtractor;

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name => "file_read";
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description => "Read a local file from the data directory.";
    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public string Category => ToolCategory.FileSystem.ToString().ToLower();
    /// <summary>
    /// Gets or sets the parameters schema.
    /// </summary>
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Relative path within the data directory" },
            "maxLines": { "type": "integer", "default": 100 }
          },
          "required": ["path"]
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemReadTool" /> class.
    /// </summary>
    /// <param name="allowedBasePath">The allowed base path.</param>
    /// <returns>The operation result.</returns>
    public FileSystemReadTool(string allowedBasePath = "/app/data", IAttachmentTextExtractionService? textExtractor = null)
    {
        _allowedBasePath = Path.GetFullPath(allowedBasePath);
        _textExtractor = textExtractor;
    }

    /// <summary>
    /// Executes the execute async operation.
    /// </summary>
    /// <param name="parametersJson">The parameters json.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var parameters = ReadParameters.Parse(parametersJson);
            var relativePath = parameters.Path;
            var maxLines = parameters.MaxLines;

            var fullPath = FileSystemPolicy.ResolveWithinBase(_allowedBasePath, relativePath);
            if (fullPath is null)
            {
                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = "Access denied: path is outside the allowed directory",
                    Duration = sw.Elapsed
                };
            }

            fullPath = ResolveExistingPath(fullPath, relativePath);
            if (!File.Exists(fullPath))
            {
                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = $"File not found: {relativePath}",
                    Duration = sw.Elapsed
                };
            }

            var content = await ReadContentAsync(fullPath, relativePath, maxLines, ct);

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = content,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = ex.Message,
                Duration = sw.Elapsed
            };
        }
    }

    private async Task<string> ReadContentAsync(string fullPath, string relativePath, int maxLines, CancellationToken ct)
    {
        if (_textExtractor is not null)
        {
            var contentType = GuessContentType(relativePath);
            if (_textExtractor.CanExtractText(contentType, relativePath))
            {
                var bytes = await File.ReadAllBytesAsync(fullPath, ct);
                var extractedText = await _textExtractor.ExtractTextAsync(contentType, relativePath, bytes, ct);
                if (!string.IsNullOrWhiteSpace(extractedText))
                    return TruncateLines(extractedText, maxLines);

                if (RequiresTextExtraction(relativePath))
                    throw new InvalidOperationException($"No text could be extracted from '{relativePath}'. Verify the document parser service is available and supports this file.");
            }
        }

        if (RequiresTextExtraction(relativePath))
            throw new InvalidOperationException($"Cannot read '{relativePath}' as plain text. Document text extraction is not available for this file.");

        var lines = await File.ReadAllLinesAsync(fullPath, ct);
        return TruncateLines(string.Join("\n", lines), maxLines);
    }

    private string ResolveExistingPath(string fullPath, string relativePath)
    {
        if (File.Exists(fullPath))
            return fullPath;

        var normalizedRelativePath = FileSystemPolicy.NormalizeRelativePath(relativePath);
        var current = _allowedBasePath;
        foreach (var segment in normalizedRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(current))
                return fullPath;

            var next = Directory
                .EnumerateFileSystemEntries(current)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), segment, StringComparison.OrdinalIgnoreCase));

            if (next is null)
                return fullPath;

            current = next;
        }

        return FileSystemPolicy.IsWithinBase(_allowedBasePath, current) ? current : fullPath;
    }

    private static string TruncateLines(string content, int maxLines)
    {
        var lines = content.Split('\n');
        var truncated = string.Join("\n", lines.Take(maxLines));
        if (lines.Length > maxLines)
            truncated += $"\n... ({lines.Length - maxLines} more lines)";

        return truncated;
    }

    private static string? GuessContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".rtf" => "application/rtf",
            ".odt" => "application/vnd.oasis.opendocument.text",
            ".epub" => "application/epub+zip",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".html" or ".htm" => "text/html",
            _ => null
        };
    }

    private static bool RequiresTextExtraction(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" or ".doc" or ".docx" or ".rtf" or ".odt" or ".epub" or ".ppt" or ".pptx" or ".xls" or ".xlsx" => true,
            _ => false
        };
    }

    private sealed record ReadParameters(string Path, int MaxLines)
    {
        public static ReadParameters Parse(string parametersJson)
        {
            const int defaultMaxLines = 100;
            using var doc = JsonDocument.Parse(parametersJson);
            var root = doc.RootElement;
            var path = root.GetProperty("path").GetString() ?? string.Empty;
            var maxLines = root.TryGetProperty("maxLines", out var maxLinesEl) && maxLinesEl.TryGetInt32(out var value)
                ? Math.Clamp(value, 1, 10_000)
                : defaultMaxLines;

            return new ReadParameters(path, maxLines);
        }
    }
}

/// <summary>
/// Represents the file system tool.
/// </summary>
[Obsolete("Use FileSystemReadTool instead.")]
public sealed class FileSystemTool : FileSystemReadTool
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemTool" /> class.
    /// </summary>
    /// <param name="allowedBasePath">The allowed base path.</param>
    public FileSystemTool(string allowedBasePath = "/app/data", IAttachmentTextExtractionService? textExtractor = null)
        : base(allowedBasePath, textExtractor)
    {
    }
}
