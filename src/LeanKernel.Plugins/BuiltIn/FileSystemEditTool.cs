using System.Text.Json;
using System.Text.RegularExpressions;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Represents the file system edit tool.
/// </summary>
[ToolMetadata(
    Name = "file_edit",
    Description = "Edit file contents via find/replace in approved paths.",
    Category = ToolCategory.FileSystem)]
public sealed class FileSystemEditTool : ITool
{
    private readonly string _allowedBasePath;
    private readonly HashSet<string> _writeAllowlist;

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name => "file_edit";
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description => "Edit a local file with literal or regex replacement in approved paths.";
    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public string Category => ToolCategory.FileSystem.ToString().ToLower();
    /// <summary>
    /// Gets or sets the parameters schema.
    /// </summary>
    public string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Relative path within the data directory" },
            "find": { "type": "string", "description": "Text or pattern to find" },
            "replace": { "type": "string", "description": "Replacement text" },
            "replaceAll": { "type": "boolean", "default": true },
            "regex": { "type": "boolean", "default": false }
          },
          "required": ["path", "find", "replace"]
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemEditTool" /> class.
    /// </summary>
    /// <param name="allowedBasePath">The allowed base path.</param>
    /// <param name="writeAllowlist">The write allowlist.</param>
    public FileSystemEditTool(string allowedBasePath = "/app/data", IEnumerable<string>? writeAllowlist = null)
    {
        _allowedBasePath = Path.GetFullPath(allowedBasePath);
        _writeAllowlist = new HashSet<string>(
            (writeAllowlist ?? FileSystemPolicy.DefaultWriteAllowlist).Select(FileSystemPolicy.NormalizeRelativePath),
            StringComparer.OrdinalIgnoreCase);
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
            using var doc = JsonDocument.Parse(parametersJson);
            var relativePath = doc.RootElement.GetProperty("path").GetString() ?? string.Empty;
            var find = doc.RootElement.GetProperty("find").GetString() ?? string.Empty;
            var replace = doc.RootElement.GetProperty("replace").GetString() ?? string.Empty;
            var replaceAll = !doc.RootElement.TryGetProperty("replaceAll", out var raEl) || raEl.GetBoolean();
            var regex = doc.RootElement.TryGetProperty("regex", out var rgxEl) && rgxEl.GetBoolean();

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

            if (!FileSystemPolicy.IsWriteAllowed(_allowedBasePath, fullPath, _writeAllowlist))
            {
                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = $"Edit denied: '{relativePath}' is not in the default write policy allowlist",
                    Duration = sw.Elapsed
                };
            }

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

            var content = await File.ReadAllTextAsync(fullPath, ct);
            string updated;
            int replacements;

            if (regex)
            {
                if (replaceAll)
                {
                    var matches = Regex.Matches(content, find);
                    replacements = matches.Count;
                    updated = Regex.Replace(content, find, replace);
                }
                else
                {
                    var regexObj = new Regex(find);
                    var match = regexObj.Match(content);
                    replacements = match.Success ? 1 : 0;
                    updated = regexObj.Replace(content, replace, 1);
                }
            }
            else
            {
                if (replaceAll)
                {
                    replacements = CountOccurrences(content, find);
                    updated = content.Replace(find, replace, StringComparison.Ordinal);
                }
                else
                {
                    var idx = content.IndexOf(find, StringComparison.Ordinal);
                    replacements = idx >= 0 ? 1 : 0;
                    updated = idx >= 0
                        ? content[..idx] + replace + content[(idx + find.Length)..]
                        : content;
                }
            }

            if (replacements == 0)
            {
                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = "No matches found to replace",
                    Duration = sw.Elapsed
                };
            }

            await File.WriteAllTextAsync(fullPath, updated, ct);

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = $"Applied {replacements} replacement(s) in {relativePath}",
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

    private static int CountOccurrences(string haystack, string needle)
    {
        if (needle.Length == 0)
            return 0;

        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }

        return count;
    }
}
