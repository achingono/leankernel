using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools.BuiltIn.FileSystem;

/// <summary>
/// Provides functionality for file chmod tool.
/// </summary>
public static class FileChmodTool
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
            Name = "file_chmod",
            Description = "Set Unix permissions on a file or directory within the allowed data directory",
            Category = "filesystem",
            Parameters =
            [
                new ToolParameter { Name = "path", Type = "string", Required = true },
                new ToolParameter { Name = "mode", Type = "string", Required = true }
            ],
            Handler = (args, ct) =>
            {
                if (OperatingSystem.IsWindows())
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_chmod", Success = false, Error = "chmod is only supported on Unix-like systems" });
                }

                var path = ToolArgumentReader.GetString(args, "path");
                var modeString = ToolArgumentReader.GetString(args, "mode");

                if (string.IsNullOrWhiteSpace(path))
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_chmod", Success = false, Error = "Path is required" });
                }

                var mode = ParseUnixMode(modeString);

                using var scope = scopeFactory.CreateScope();
                var config = scope.ServiceProvider.GetRequiredService<IOptions<LeanKernelConfig>>().Value.FileSystem;
                var fullPath = FileSystemSupport.ResolveWithinRoot(config.AllowedRoot, path);
                if (fullPath is null)
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_chmod", Success = false, Error = "Access denied: path is outside the allowed directory" });
                }

                if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                {
                    return Task.FromResult(new ToolResult { ToolName = "file_chmod", Success = false, Error = $"Path not found: {path}" });
                }

                File.SetUnixFileMode(fullPath, mode);
                return Task.FromResult(new ToolResult { ToolName = "file_chmod", Success = true, Output = $"Updated mode for {path} to {modeString}" });
            }
        };
    }

    private static UnixFileMode ParseUnixMode(string mode)
    {
        var trimmed = mode.Trim();
        if (trimmed.Length == 4 && trimmed[0] == '0')
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length != 3 || trimmed.Any(ch => ch is < '0' or > '7'))
        {
            throw new ArgumentException($"Invalid mode '{mode}'. Expected octal string like 644 or 755.");
        }

        var owner = trimmed[0] - '0';
        var group = trimmed[1] - '0';
        var other = trimmed[2] - '0';

        return ToMode(owner, group, other);
    }

    private static UnixFileMode ToMode(int owner, int group, int other)
    {
        UnixFileMode mode = 0;
        mode |= MapTriplet(owner, UnixFileMode.UserRead, UnixFileMode.UserWrite, UnixFileMode.UserExecute);
        mode |= MapTriplet(group, UnixFileMode.GroupRead, UnixFileMode.GroupWrite, UnixFileMode.GroupExecute);
        mode |= MapTriplet(other, UnixFileMode.OtherRead, UnixFileMode.OtherWrite, UnixFileMode.OtherExecute);
        return mode;
    }

    private static UnixFileMode MapTriplet(int value, UnixFileMode read, UnixFileMode write, UnixFileMode execute)
    {
        UnixFileMode result = 0;
        if ((value & 4) != 0) result |= read;
        if ((value & 2) != 0) result |= write;
        if ((value & 1) != 0) result |= execute;
        return result;
    }
}
