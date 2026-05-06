using System.Text;

namespace LeanKernel.Core.Models;

/// <summary>
/// Represents an attachment supplied alongside an inbound message.
/// </summary>
public sealed record InboundAttachment
{
    public required string Id { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
    public long? Size { get; init; }
    public string? Caption { get; init; }
    public string? ExtractedText { get; init; }
}

/// <summary>
/// Extracts readable text from inbound attachment bytes when the content appears text-like.
/// </summary>
public static class InboundAttachmentTextExtractor
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

        text = NormalizeText(text);
        if (string.IsNullOrWhiteSpace(text) || LooksBinary(text))
            return null;

        return text.Length <= MaxExtractedCharacters
            ? text
            : text[..MaxExtractedCharacters] + "\n...[truncated]";
    }

    public static string NormalizeText(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

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

/// <summary>
/// Combines message text and inbound attachments into prompt-ready content plus metadata.
/// </summary>
public static class InboundMessageContentFormatter
{
    public static string FormatContent(string? body, IReadOnlyList<InboundAttachment> attachments)
    {
        var trimmedBody = body?.Trim();
        if (attachments.Count == 0)
            return trimmedBody ?? string.Empty;

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(trimmedBody))
        {
            sb.AppendLine(trimmedBody);
            sb.AppendLine();
        }

        sb.AppendLine(attachments.Count == 1
            ? "Received 1 attachment:"
            : $"Received {attachments.Count} attachments:");

        foreach (var attachment in attachments)
        {
            var displayName = attachment.FileName ?? attachment.Id;
            var contentType = attachment.ContentType ?? "unknown";
            sb.Append("- ")
                .Append(displayName)
                .Append(" (")
                .Append(contentType);

            if (attachment.Size is { } size)
                sb.Append(", ").Append(size).Append(" bytes");

            sb.Append(')').AppendLine();

            if (!string.IsNullOrWhiteSpace(attachment.Caption))
                sb.AppendLine($"Caption: {attachment.Caption}");

            if (!string.IsNullOrWhiteSpace(attachment.ExtractedText))
            {
                sb.AppendLine($"Attachment text from {displayName}:");
                sb.AppendLine($"--- Begin attachment: {displayName} ---");
                sb.AppendLine(attachment.ExtractedText);
                sb.AppendLine($"--- End attachment: {displayName} ---");
            }
            else
            {
                sb.AppendLine("Attachment content could not be extracted automatically.");
            }

            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    public static Dictionary<string, string> BuildMetadata(
        string metadataPrefix,
        IReadOnlyList<InboundAttachment> attachments)
    {
        if (attachments.Count == 0)
            return [];

        return new Dictionary<string, string>
        {
            [$"{metadataPrefix}:attachment_count"] = attachments.Count.ToString(),
            [$"{metadataPrefix}:attachment_text_count"] = attachments.Count(a => !string.IsNullOrWhiteSpace(a.ExtractedText)).ToString(),
            [$"{metadataPrefix}:attachments"] = string.Join("; ", attachments.Select(attachment =>
                $"{attachment.FileName ?? attachment.Id}|{attachment.ContentType ?? "unknown"}"))
        };
    }
}
