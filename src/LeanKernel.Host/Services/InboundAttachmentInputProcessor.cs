using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Host.Services;

/// <summary>
/// Converts API attachment payloads into shared inbound attachment models.
/// </summary>
public sealed class InboundAttachmentInputProcessor
{
    private readonly IAttachmentTextExtractionService _textExtractor;

    public InboundAttachmentInputProcessor(IAttachmentTextExtractionService textExtractor)
    {
        _textExtractor = textExtractor;
    }

    public async Task<IReadOnlyList<InboundAttachment>> ProcessAsync(
        IReadOnlyList<InboundAttachmentInput>? attachments,
        CancellationToken ct)
    {
        if (attachments is null || attachments.Count == 0)
            return [];

        var processed = new List<InboundAttachment>(attachments.Count);
        for (var i = 0; i < attachments.Count; i++)
        {
            var attachment = attachments[i];
            string? extractedText = null;

            if (!string.IsNullOrWhiteSpace(attachment.Text))
            {
                extractedText = InboundAttachmentTextExtractor.NormalizeText(attachment.Text);
                if (string.IsNullOrWhiteSpace(extractedText))
                    extractedText = null;
            }
            else if (!string.IsNullOrWhiteSpace(attachment.Base64Content))
            {
                try
                {
                    var bytes = Convert.FromBase64String(ExtractBase64Payload(attachment.Base64Content));
                    extractedText = await _textExtractor.ExtractTextAsync(
                        attachment.ContentType,
                        attachment.FileName,
                        bytes,
                        ct);
                }
                catch (FormatException ex)
                {
                    throw new ArgumentException(
                        $"Attachment '{attachment.FileName ?? attachment.Id ?? $"attachment-{i + 1}"}' does not contain valid base64 data.",
                        ex);
                }
            }

            processed.Add(new InboundAttachment
            {
                Id = attachment.Id ?? $"attachment-{i + 1}",
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                Caption = attachment.Caption,
                Size = attachment.Size,
                ExtractedText = extractedText
            });
        }

        return processed;
    }

    private static string ExtractBase64Payload(string base64Content)
    {
        var commaIndex = base64Content.IndexOf(',');
        if (commaIndex >= 0 && base64Content[..commaIndex].Contains(";base64", StringComparison.OrdinalIgnoreCase))
            return base64Content[(commaIndex + 1)..];

        return base64Content.Trim();
    }
}

public sealed class InboundAttachmentInput
{
    public string? Id { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
    public long? Size { get; init; }
    public string? Caption { get; init; }
    public string? Text { get; init; }
    public string? Base64Content { get; init; }
}
