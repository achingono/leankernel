using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Host.Services;

/// <summary>
/// Sandboxed file browser. All paths are restricted to the data directory.
/// </summary>
public sealed class FileBrowserService
{
    private readonly string _rootPath;

    public FileBrowserService(IOptions<LeanKernelConfig> config)
    {
        var wikiPath = config.Value.Wiki.BasePath;
        // Root is the data directory (parent of wiki)
        _rootPath = Path.GetFullPath(
            Path.GetDirectoryName(wikiPath) ?? "/app/data");
    }

    public FileBrowseResult Browse(string? relativePath = null)
    {
        var targetPath = ResolveSafePath(relativePath);
        if (targetPath is null || !Directory.Exists(targetPath))
            return new FileBrowseResult { Error = "Directory not found" };

        var entries = new List<FileSystemItem>();

        foreach (var dir in Directory.GetDirectories(targetPath).OrderBy(d => d))
        {
            entries.Add(new FileSystemItem
            {
                Name = Path.GetFileName(dir),
                Path = Path.GetRelativePath(_rootPath, dir).Replace('\\', '/'),
                IsDirectory = true,
                Size = 0
            });
        }

        foreach (var file in Directory.GetFiles(targetPath).OrderBy(f => f))
        {
            var info = new FileInfo(file);
            entries.Add(new FileSystemItem
            {
                Name = info.Name,
                Path = Path.GetRelativePath(_rootPath, file).Replace('\\', '/'),
                IsDirectory = false,
                Size = info.Length,
                ModifiedAt = info.LastWriteTimeUtc
            });
        }

        return new FileBrowseResult
        {
            CurrentPath = relativePath ?? "",
            Entries = entries
        };
    }

    public async Task<FileReadResult> ReadAsync(string relativePath, CancellationToken ct)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (fullPath is null || !File.Exists(fullPath))
            return new FileReadResult { Error = "File not found" };

        var info = new FileInfo(fullPath);
        if (info.Length > 1_000_000) // 1MB limit
            return new FileReadResult { Error = "File too large (>1MB)" };

        var content = await File.ReadAllTextAsync(fullPath, ct);
        return new FileReadResult
        {
            Path = relativePath,
            Content = content,
            Size = info.Length,
            Extension = info.Extension
        };
    }

    private string? ResolveSafePath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return _rootPath;

        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));

        // Sandbox check — must stay under root
        if (!fullPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            return null;

        return fullPath;
    }
}

public sealed class FileBrowseResult
{
    public string CurrentPath { get; init; } = "";
    public List<FileSystemItem> Entries { get; init; } = [];
    public string? Error { get; init; }
}

public sealed class FileSystemItem
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime? ModifiedAt { get; init; }
}

public sealed class FileReadResult
{
    public string? Path { get; init; }
    public string? Content { get; init; }
    public long Size { get; init; }
    public string? Extension { get; init; }
    public string? Error { get; init; }
}
