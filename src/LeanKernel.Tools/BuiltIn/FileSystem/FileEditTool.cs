using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools.BuiltIn.FileSystem;

/// <summary>
/// Provides functionality for file edit tool.
/// </summary>
public static class FileEditTool
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    [SuppressMessage("Major Code Smell", "S3776", Justification = "Replacement logic is intentionally explicit and bounded.")]
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
            Name = "file_edit",
            Description = "Edit file contents by replacing text or regex matches within the allowed data directory",
            Category = "filesystem",
            Parameters =
            [
                new ToolParameter { Name = "path", Type = "string", Required = true },
                new ToolParameter { Name = "find", Type = "string", Required = true },
                new ToolParameter { Name = "replace", Type = "string", Required = true },
                new ToolParameter { Name = "replaceAll", Type = "boolean", Required = false },
                new ToolParameter { Name = "regex", Type = "boolean", Required = false }
            ],
            Handler = async (args, ct) =>
            {
                var path = ToolArgumentReader.GetString(args, "path");
                var find = ToolArgumentReader.GetString(args, "find");
                var replace = ToolArgumentReader.GetString(args, "replace");
                var replaceAll = ToolArgumentReader.GetBoolOrDefault(args, "replaceAll", true);
                var regex = ToolArgumentReader.GetBoolOrDefault(args, "regex", false);

                if (string.IsNullOrWhiteSpace(path))
                {
                    return new ToolResult { ToolName = "file_edit", Success = false, Error = "Path is required" };
                }

                using var scope = scopeFactory.CreateScope();
                var config = scope.ServiceProvider.GetRequiredService<IOptions<LeanKernelConfig>>().Value.FileSystem;
                var fullPath = FileSystemSupport.ResolveWithinRoot(config.AllowedRoot, path);
                if (fullPath is null)
                {
                    return new ToolResult { ToolName = "file_edit", Success = false, Error = "Access denied: path is outside the allowed directory" };
                }

                if (!File.Exists(fullPath))
                {
                    return new ToolResult { ToolName = "file_edit", Success = false, Error = $"File not found: {path}" };
                }

                var content = await File.ReadAllTextAsync(fullPath, ct);
                string updated;
                int replacements;

                if (regex)
                {
                    if (replaceAll)
                    {
                        replacements = Regex.Matches(content, find, RegexOptions.None, RegexTimeout).Count;
                        updated = Regex.Replace(content, find, replace, RegexOptions.None, RegexTimeout);
                    }
                    else
                    {
                        var re = new Regex(find, RegexOptions.None, RegexTimeout);
                        replacements = re.IsMatch(content) ? 1 : 0;
                        updated = re.Replace(content, replace, 1);
                    }
                }
                else if (replaceAll)
                {
                    replacements = CountOccurrences(content, find);
                    updated = content.Replace(find, replace, StringComparison.Ordinal);
                }
                else
                {
                    var index = content.IndexOf(find, StringComparison.Ordinal);
                    replacements = index >= 0 ? 1 : 0;
                    updated = index >= 0 ? content[..index] + replace + content[(index + find.Length)..] : content;
                }

                if (replacements == 0)
                {
                    return new ToolResult { ToolName = "file_edit", Success = false, Error = "No matches found to replace" };
                }

                await File.WriteAllTextAsync(fullPath, updated, ct);
                return new ToolResult { ToolName = "file_edit", Success = true, Output = $"Applied {replacements} replacement(s) in {path}" };
            }
        };
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (needle.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
