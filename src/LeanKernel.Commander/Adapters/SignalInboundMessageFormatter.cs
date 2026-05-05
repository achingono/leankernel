using System.Text;

namespace LeanKernel.Commander.Adapters;

/// <summary>
/// Formats Signal inbound messages so attachment contents become usable prompt context.
/// </summary>
public static class SignalInboundMessageFormatter
{
    public static string FormatContent(string? body, IReadOnlyList<SignalAttachmentInfo> attachments)
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

    public static Dictionary<string, string> BuildMetadata(IReadOnlyList<SignalAttachmentInfo> attachments)
    {
        if (attachments.Count == 0)
            return [];

        return new Dictionary<string, string>
        {
            ["signal:attachment_count"] = attachments.Count.ToString(),
            ["signal:attachment_text_count"] = attachments.Count(a => !string.IsNullOrWhiteSpace(a.ExtractedText)).ToString(),
            ["signal:attachments"] = string.Join("; ", attachments.Select(attachment =>
                $"{attachment.FileName ?? attachment.Id}|{attachment.ContentType ?? "unknown"}"))
        };
    }
}
