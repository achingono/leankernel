using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Host.Services;

/// <summary>
/// Sandboxed file browser. All paths are restricted to the data directory.
/// </summary>
public sealed class FileBrowserService
{
    private readonly string _rootPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileBrowserService" /> class.
    /// </summary>
    /// <param name="config">The config.</param>
    public FileBrowserService(IOptions<LeanKernelConfig> config)
    {
        var wikiPath = config.Value.Wiki.BasePath;
        // Root is the data directory (parent of wiki)
        _rootPath = Path.GetFullPath(
            Path.GetDirectoryName(wikiPath) ?? "/app/data");
    }

    /// <summary>
    /// Executes the browse operation.
    /// </summary>
    /// <param name="relativePath">The relative path.</param>
    /// <returns>The operation result.</returns>
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

    /// <summary>
    /// Executes the read async operation.
    /// </summary>
    /// <param name="relativePath">The relative path.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
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

/// <summary>
/// Represents the file browse result.
/// </summary>
public sealed class FileBrowseResult
{
    /// <summary>
    /// Gets or sets the current path.
    /// </summary>
    public string CurrentPath { get; init; } = "";
    /// <summary>
    /// Gets or sets the entries.
    /// </summary>
    public List<FileSystemItem> Entries { get; init; } = [];
    /// <summary>
    /// Gets or sets the error.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Represents the file system item.
/// </summary>
public sealed class FileSystemItem
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Gets or sets the path.
    /// </summary>
    public required string Path { get; init; }
    /// <summary>
    /// Gets or sets the is directory.
    /// </summary>
    public required bool IsDirectory { get; init; }
    /// <summary>
    /// Gets or sets the size.
    /// </summary>
    public long Size { get; init; }
    /// <summary>
    /// Gets or sets the modified at.
    /// </summary>
    public DateTime? ModifiedAt { get; init; }
}

/// <summary>
/// Represents the file read result.
/// </summary>
public sealed class FileReadResult
{
    /// <summary>
    /// Gets or sets the path.
    /// </summary>
    public string? Path { get; init; }
    /// <summary>
    /// Gets or sets the content.
    /// </summary>
    public string? Content { get; init; }
    /// <summary>
    /// Gets or sets the size.
    /// </summary>
    public long Size { get; init; }
    /// <summary>
    /// Gets or sets the extension.
    /// </summary>
    public string? Extension { get; init; }
    /// <summary>
    /// Gets or sets the error.
    /// </summary>
    public string? Error { get; init; }
}
