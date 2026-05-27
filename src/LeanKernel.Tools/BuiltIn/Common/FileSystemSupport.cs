using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LeanKernel.Abstractions.Configuration;

namespace LeanKernel.Tools.BuiltIn.Common;

internal static class FileSystemSupport
{
    private static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public static string NormalizeRelativePath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').TrimStart('/');
    }

    public static string? ResolveWithinRoot(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, NormalizeRelativePath(relativePath)));

        if (!IsWithinRoot(normalizedRoot, candidate))
        {
            return null;
        }

        return HasSymlinkSegment(normalizedRoot, candidate) ? null : candidate;
    }

    public static bool IsWithinRoot(string root, string candidate)
    {
        var normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.Equals(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTextLikeExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        return Path.GetExtension(path).ToLowerInvariant() is ".txt" or ".md" or ".json" or ".xml" or ".csv" or ".html" or ".htm" or ".yaml" or ".yml" or ".log" or ".ini" or ".cfg" or ".cs" or ".jsonl";
    }

    public static bool IsOcrCandidate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".tif" or ".pdf";
    }

    public static string EnsureScratchPath(FileSystemConfig config, string extension)
    {
        var scratchRoot = Path.GetFullPath(config.ScratchRoot);
        Directory.CreateDirectory(scratchRoot);
        return Path.Combine(scratchRoot, $"{Guid.NewGuid():N}{extension}");
    }

    public static async Task<string> RunPythonAsync(
        FileSystemConfig config,
        string script,
        IReadOnlyCollection<string> arguments,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = config.PythonExecutable,
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

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    [SuppressMessage("Major Code Smell", "S1066", Justification = "The directory walk is easier to audit with explicit path checks.")]
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
            if (Directory.Exists(current) || File.Exists(current))
            {
                if (IsReparsePoint(current))
                {
                    return true;
                }
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

internal static class TextExtractionHelper
{
    public static async Task<string> ExtractAsync(string path, FileSystemConfig config, CancellationToken ct)
    {
        if (FileSystemSupport.IsTextLikeExtension(path))
        {
            var text = await File.ReadAllTextAsync(path, ct);
            return Truncate(text, config.MaxExtractedCharacters);
        }

        if (!FileSystemSupport.IsOcrCandidate(path))
        {
            throw new InvalidOperationException($"Unsupported file type '{Path.GetExtension(path)}' for text extraction.");
        }

        var script = """
import json
import sys
from pathlib import Path

from paddleocr import PaddleOCR

try:
    from pdf2image import convert_from_path
except Exception:
    convert_from_path = None

path = Path(sys.argv[1])
ocr = PaddleOCR(use_angle_cls=True, lang='en')

def collect_text(result):
    lines = []
    if not result:
        return lines
    for block in result:
        if not block:
            continue
        for item in block:
            if not item or len(item) < 2:
                continue
            text = item[1][0] if isinstance(item[1], (list, tuple)) and item[1] else ""
            if text:
                lines.append(text)
    return lines

if path.suffix.lower() == '.pdf':
    if convert_from_path is None:
        raise SystemExit('pdf2image is not available.')
    pages = convert_from_path(str(path))
    all_lines = []
    for page in pages:
        all_lines.extend(collect_text(ocr.ocr(page, cls=True)))
    print("\n".join(all_lines))
else:
    print("\n".join(collect_text(ocr.ocr(str(path), cls=True))))
""";

        var output = await FileSystemSupport.RunPythonAsync(config, script, [path], ct);
        return Truncate(output, config.MaxExtractedCharacters);
    }

    private static string Truncate(string value, int limit)
    {
        if (value.Length <= limit)
        {
            return value;
        }

        return value[..limit] + "\n\n[Content truncated to " + limit + " characters.]";
    }
}
