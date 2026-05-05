using System.Text;

namespace LeanKernel.Commander.Adapters;

/// <summary>
/// Extracts readable text from Signal attachment payloads for prompt assembly.
/// </summary>
public static class SignalAttachmentTextExtractor
{
    private const int MaxExtractedCharacters = 12_000;

    private static readonly HashSet<string> TextMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/json",
        "application/ld+json",
        "application/xml",
        "application/xhtml+xml",
        "application/x-yaml",
        "application/yaml",
        "application/toml",
        "application/x-sh",
        "application/javascript"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".md",
        ".markdown",
        ".csv",
        ".tsv",
        ".json",
        ".jsonl",
        ".xml",
        ".yaml",
        ".yml",
        ".html",
        ".htm",
        ".css",
        ".js",
        ".ts",
        ".log",
        ".ini",
        ".cfg",
        ".conf",
        ".toml",
        ".sh"
    };

    public static string? TryExtractText(string? contentType, string? fileName, byte[] bytes)
    {
        if (bytes.Length == 0 || !IsTextLike(contentType, fileName))
            return null;

        string text;
        try
        {
            text = Decode(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (string.IsNullOrWhiteSpace(text) || LooksBinary(text))
            return null;

        return text.Length <= MaxExtractedCharacters
            ? text
            : text[..MaxExtractedCharacters] + "\n...[truncated]";
    }

    private static bool IsTextLike(string? contentType, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                return true;

            if (contentType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
                || contentType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
                || TextMimeTypes.Contains(contentType))
            {
                return true;
            }
        }

        var extension = Path.GetExtension(fileName ?? string.Empty);
        return !string.IsNullOrWhiteSpace(extension) && TextExtensions.Contains(extension);
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2);

        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static bool LooksBinary(string text)
    {
        var controlCharacters = text.Count(ch =>
            char.IsControl(ch) && ch is not '\n' and not '\t');

        return controlCharacters > Math.Max(3, text.Length / 20);
    }
}
