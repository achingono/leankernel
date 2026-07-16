using System.Text;

namespace LeanKernel.Channels.Signal;

public static class AttachmentParser
{
    public static IReadOnlyList<string> ParseAttachmentHints(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.StartsWith("attachment://", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string AppendAttachmentContext(
        string text,
        IReadOnlyList<InboundAttachment> attachments,
        IReadOnlyList<string> attachmentHints)
    {
        if (attachments.Count == 0 && attachmentHints.Count == 0)
            return text;

        var builder = new StringBuilder(text);
        if (builder.Length > 0)
            builder.AppendLine();

        builder.AppendLine();
        builder.AppendLine("[channel_attachment_context]");

        if (attachments.Count > 0)
        {
            builder.AppendLine($"attachment_count={attachments.Count}");
            builder.AppendLine($"image_attachment_count={attachments.Count(attachment => attachment.IsImage)}");
            builder.AppendLine($"image_bytes_forwarded_count={attachments.Count(attachment => !string.IsNullOrWhiteSpace(attachment.ImageDataUrl))}");

            foreach (var attachment in attachments.Take(5))
            {
                var mediaType = string.IsNullOrWhiteSpace(attachment.ContentType)
                    ? "unknown"
                    : attachment.ContentType;
                var fileName = string.IsNullOrWhiteSpace(attachment.FileName)
                    ? "unknown"
                    : attachment.FileName;
                var hasImageBytes = string.IsNullOrWhiteSpace(attachment.ImageDataUrl) ? "no" : "yes";
                builder.AppendLine($"attachment: content_type={mediaType}; file_name={fileName}; image_bytes_forwarded={hasImageBytes}");
            }
        }

        if (attachmentHints.Count > 0)
        {
            builder.AppendLine($"attachment_hint_count={attachmentHints.Count}");

            foreach (var attachmentHint in attachmentHints.Take(5))
            {
                builder.AppendLine($"attachment_hint: {attachmentHint}");
            }
        }

        builder.Append("[/channel_attachment_context]");
        return builder.ToString();
    }

    public static object BuildGatewayInput(
        string text,
        IReadOnlyList<InboundAttachment> attachments,
        IReadOnlyList<string> attachmentHints)
    {
        if (attachments.Count == 0 && attachmentHints.Count == 0)
            return text;

        var content = new List<object>();
        var textWithContext = AppendAttachmentContext(text, attachments, attachmentHints);

        if (!string.IsNullOrWhiteSpace(textWithContext))
        {
            content.Add(new
            {
                type = "input_text",
                text = textWithContext
            });
        }

        foreach (var attachment in attachments
                     .Where(attachment => !string.IsNullOrWhiteSpace(attachment.ImageDataUrl))
                     .Take(3))
        {
            content.Add(new
            {
                type = "input_image",
                image_url = attachment.ImageDataUrl
            });
        }

        if (content.Count == 0)
        {
            content.Add(new
            {
                type = "input_text",
                text = "[Signal message contained attachment metadata but no text body.]"
            });
        }

        return new[]
        {
            new
            {
                role = "user",
                content
            }
        };
    }
}
