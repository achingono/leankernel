using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

[ToolMetadata(
    Name = "file_chmod",
    Description = "Change Unix permissions for approved files/directories.",
    Category = ToolCategory.FileSystem)]
public sealed class FileSystemChmodTool : ITool
{
    private readonly string _allowedBasePath;
    private readonly HashSet<string> _writeAllowlist;

    public string Name => "file_chmod";
    public string Description => "Set Unix file mode (octal, e.g. 644 or 755) for approved paths.";
    public string Category => ToolCategory.FileSystem.ToString().ToLower();
    public string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string" },
            "mode": { "type": "string", "description": "Octal mode like 644, 755, or 0644" }
          },
          "required": ["path", "mode"]
        }
        """;

    public FileSystemChmodTool(string allowedBasePath = "/app/data", IEnumerable<string>? writeAllowlist = null)
    {
        _allowedBasePath = Path.GetFullPath(allowedBasePath);
        _writeAllowlist = new HashSet<string>(
            (writeAllowlist ?? FileSystemPolicy.DefaultWriteAllowlist).Select(FileSystemPolicy.NormalizeRelativePath),
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (OperatingSystem.IsWindows())
            {
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = "chmod is only supported on Unix-like systems",
                    Duration = sw.Elapsed
                });
            }

            using var doc = JsonDocument.Parse(parametersJson);
            var relativePath = doc.RootElement.GetProperty("path").GetString() ?? string.Empty;
            var modeString = doc.RootElement.GetProperty("mode").GetString() ?? string.Empty;

            var fullPath = FileSystemPolicy.ResolveWithinBase(_allowedBasePath, relativePath);
            if (fullPath is null)
            {
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = "Access denied: path is outside the allowed directory",
                    Duration = sw.Elapsed
                });
            }

            if (!FileSystemPolicy.IsWriteAllowed(_allowedBasePath, fullPath, _writeAllowlist))
            {
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = $"chmod denied: '{relativePath}' is not in the default write policy allowlist",
                    Duration = sw.Elapsed
                });
            }

            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                return Task.FromResult(new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = $"Path not found: {relativePath}",
                    Duration = sw.Elapsed
                });
            }

            var mode = ParseUnixMode(modeString);
            File.SetUnixFileMode(fullPath, mode);

            return Task.FromResult(new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = $"Updated mode for {relativePath} to {modeString}",
                Duration = sw.Elapsed
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = ex.Message,
                Duration = sw.Elapsed
            });
        }
    }

    private static UnixFileMode ParseUnixMode(string mode)
    {
        var trimmed = mode.Trim();
        if (trimmed.Length == 4 && trimmed[0] == '0')
            trimmed = trimmed[1..];

        if (trimmed.Length != 3 || trimmed.Any(ch => ch is < '0' or > '7'))
            throw new ArgumentException($"Invalid mode '{mode}'. Expected octal string like 644 or 755.");

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