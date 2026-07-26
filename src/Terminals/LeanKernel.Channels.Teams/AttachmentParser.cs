using System.Text;

using LeanKernel.Channels.Teams.Models;
using LeanKernel.Entities;

namespace LeanKernel.Channels.Teams;

/// <summary>Parses Teams attachments and builds gateway-compatible inputs.</summary>
public static class AttachmentParser
{
    /// <summary>Parses Teams webhook attachments into normalized channel envelopes.</summary>
    /// <param name="attachments">The raw Teams attachments.</param>
    /// <returns>A deduplicated list of normalized attachment envelopes.</returns>
    public static IReadOnlyList<ChannelAttachmentEnvelope> Parse(IReadOnlyList<Attachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return [];
        }

        return attachments
            .Select(ToEnvelope)
            .Where(IsMeaningful)
            .GroupBy(a => $"{a.AttachmentId}|{a.FileName}|{a.ContentType}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>Builds a gateway input payload from text and normalized attachments.</summary>
    /// <param name="text">The inbound message text.</param>
    /// <param name="attachments">The normalized channel attachments.</param>
    /// <returns>A gateway input object.</returns>
    public static object BuildGatewayInput(string text, IReadOnlyList<ChannelAttachmentEnvelope> attachments)
    {
        if (attachments.Count == 0)
        {
            return text;
        }

        var content = new List<object>();
        var textWithContext = AppendAttachmentContext(text, attachments);

        if (!string.IsNullOrWhiteSpace(textWithContext))
        {
            content.Add(new
            {
                type = "input_text",
                text = textWithContext,
            });
        }

        foreach (var attachment in attachments.Where(a => a.HasImageData).Take(3))
        {
            content.Add(new
            {
                type = "input_image",
                image_url = attachment.ImageDataUrl,
            });
        }

        if (content.Count == 0)
        {
            content.Add(new
            {
                type = "input_text",
                text = "[Teams message contained attachment metadata but no text body.]",
            });
        }

        return new[]
        {
            new
            {
                role = "user",
                content,
            },
        };
    }

    private static ChannelAttachmentEnvelope ToEnvelope(Attachment attachment)
    {
        var attachmentId = attachment.ContentUrl ?? string.Empty;
        var contentType = attachment.ContentType ?? string.Empty;
        var fileName = attachment.Name ?? string.Empty;
        var imageDataUrl = string.Empty;
        var fileDataUrl = string.Empty;

        if (!string.IsNullOrWhiteSpace(attachment.ContentUrl)
            && attachment.ContentUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            if (contentType.Length == 0)
            {
                contentType = ResolveDataUrlContentType(attachment.ContentUrl);
            }

            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                imageDataUrl = attachment.ContentUrl;
            }
            else
            {
                fileDataUrl = attachment.ContentUrl;
            }
        }

        return new ChannelAttachmentEnvelope(
            attachmentId,
            contentType,
            fileName,
            imageDataUrl,
            fileDataUrl);
    }

    private static string AppendAttachmentContext(string text, IReadOnlyList<ChannelAttachmentEnvelope> attachments)
    {
        if (attachments.Count == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text);
        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("[channel_attachment_context]");
        builder.AppendLine($"attachment_count={attachments.Count}");
        builder.AppendLine($"image_attachment_count={attachments.Count(a => a.IsImage)}");
        builder.AppendLine($"image_bytes_forwarded_count={attachments.Count(a => a.HasImageData)}");
        builder.AppendLine($"document_attachment_count={attachments.Count(a => !a.IsImage)}");
        builder.AppendLine($"document_ingestion_queued_count={attachments.Count(a => !a.IsImage && a.HasFileData)}");

        foreach (var attachment in attachments.Take(5))
        {
            var mediaType = string.IsNullOrWhiteSpace(attachment.ContentType) ? "unknown" : attachment.ContentType;
            var fileName = string.IsNullOrWhiteSpace(attachment.FileName) ? "unknown" : attachment.FileName;
            var hasImageBytes = attachment.HasImageData ? "yes" : "no";
            var hasFileBytes = attachment.HasFileData ? "yes" : "no";
            builder.AppendLine($"attachment: content_type={mediaType}; file_name={fileName}; image_bytes_forwarded={hasImageBytes}; file_bytes_forwarded={hasFileBytes}");
        }

        builder.Append("[/channel_attachment_context]");
        return builder.ToString();
    }

    private static string ResolveDataUrlContentType(string dataUrl)
    {
        var semicolonIndex = dataUrl.IndexOf(';', StringComparison.Ordinal);
        if (semicolonIndex <= 5)
        {
            return string.Empty;
        }

        return dataUrl[5..semicolonIndex];
    }

    private static bool IsMeaningful(ChannelAttachmentEnvelope attachment)
        => !string.IsNullOrWhiteSpace(attachment.AttachmentId)
           || !string.IsNullOrWhiteSpace(attachment.FileName)
           || !string.IsNullOrWhiteSpace(attachment.ContentType)
           || attachment.HasFileData
           || attachment.HasImageData;
}