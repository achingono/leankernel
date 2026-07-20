using System.Diagnostics;
using System.Text;

namespace LeanKernel.Logic.Tools.BuiltIn;

/// <summary>
/// Provides filesystem safety helpers for file tools.
/// </summary>
public static class FileSystemSupport
{
    private static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>
    /// Resolves a candidate path within the allowed root, returning null when the
    /// resolved path would escape the root boundary.
    /// </summary>
    /// <param name="rootPath">The allowed root path.</param>
    /// <param name="subPath">The relative sub-path to resolve.</param>
    /// <returns>The resolved absolute path, or null if it escapes the root.</returns>
    public static string? ResolveWithinRoot(string rootPath, string? subPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        var root = Path.GetFullPath(rootPath);

        string candidate;
        if (string.IsNullOrWhiteSpace(subPath))
        {
            candidate = root;
        }
        else
        {
            candidate = Path.GetFullPath(Path.Combine(root, subPath));
        }

        if (!IsWithinRoot(root, candidate))
        {
            return null;
        }

        if (HasSymlinkSegment(root, candidate))
        {
            return null;
        }

        return candidate;
    }

    /// <summary>
    /// Returns true when <paramref name="candidate"/> is at or beneath <paramref name="root"/>.
    /// </summary>
    /// <param name="root">The allowed root path.</param>
    /// <param name="candidate">The candidate path to check.</param>
    /// <returns>True when the candidate is within the root; false otherwise.</returns>
    public static bool IsWithinRoot(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedCandidate, Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when the path has a text-like extension suitable for direct reading.
    /// </summary>
    /// <param name="path">The file path to inspect.</param>
    /// <returns>True when the extension is text-like; false otherwise.</returns>
    public static bool IsTextLikeExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        return Path.GetExtension(path).ToLowerInvariant() is
            ".txt" or ".md" or ".json" or ".xml" or ".csv" or ".html" or ".htm" or
            ".yaml" or ".yml" or ".log" or ".ini" or ".cfg" or ".cs" or ".jsonl" or
            ".ts" or ".js" or ".py" or ".sh" or ".css" or ".sql" or ".toml" or ".env" or "";
    }

    /// <summary>
    /// Returns true when the file is a candidate for OCR extraction.
    /// </summary>
    /// <param name="path">The file path to inspect.</param>
    /// <returns>True when the file is an OCR candidate; false otherwise.</returns>
    public static bool IsOcrCandidate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return Path.GetExtension(path).ToLowerInvariant() is
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".tif" or ".pdf";
    }

    /// <summary>
    /// Returns true when the file is an EPUB candidate.
    /// </summary>
    /// <param name="path">The file path to inspect.</param>
    /// <returns>True when the file is an EPUB candidate; false otherwise.</returns>
    public static bool IsEpubCandidate(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && string.Equals(Path.GetExtension(path), ".epub", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when the file is a DOCX candidate.
    /// </summary>
    /// <param name="path">The file path to inspect.</param>
    /// <returns>True when the file is a DOCX candidate; false otherwise.</returns>
    public static bool IsDocxCandidate(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when the file is a PPTX candidate.
    /// </summary>
    /// <param name="path">The file path to inspect.</param>
    /// <returns>True when the file is a PPTX candidate; false otherwise.</returns>
    public static bool IsPptxCandidate(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && string.Equals(Path.GetExtension(path), ".pptx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a scratch file path in the configured scratch directory.
    /// </summary>
    /// <param name="scratchRoot">The scratch directory root path.</param>
    /// <param name="extension">The file extension (e.g. ".pdf").</param>
    /// <returns>The absolute scratch file path.</returns>
    public static string EnsureScratchPath(string scratchRoot, string extension)
    {
        var root = Path.GetFullPath(scratchRoot);
        Directory.CreateDirectory(root);
        return Path.Combine(root, $"{Guid.NewGuid():N}{extension}");
    }

    /// <summary>
    /// Runs a Python script with arguments and returns stdout.
    /// </summary>
    /// <param name="pythonExecutable">The path to the Python interpreter.</param>
    /// <param name="script">The Python script content.</param>
    /// <param name="arguments">The script arguments.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stdout output from the Python script.</returns>
    public static async Task<string> RunPythonAsync(
        string pythonExecutable,
        string script,
        IReadOnlyCollection<string> arguments,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-");
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start Python process.");
        }

        await process.StandardInput.WriteAsync(script);
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(ct);

        stdout.Append(await outputTask);
        stderr.Append(await errorTask);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr.ToString())
                ? "Python extraction process failed."
                : stderr.ToString().Trim());
        }

        return stdout.ToString();
    }

    private static bool HasSymlinkSegment(string root, string candidate)
    {
        var current = Path.GetFullPath(root);
        var relative = Path.GetRelativePath(current, candidate);
        if (relative == ".")
        {
            return IsReparsePoint(current);
        }

        foreach (var segment in relative.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((Directory.Exists(current) || File.Exists(current)) && IsReparsePoint(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;
        }
    }
}