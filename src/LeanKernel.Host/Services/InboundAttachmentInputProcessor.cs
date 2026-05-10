using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Host.Services;

/// <summary>
/// Converts API attachment payloads into shared inbound attachment models.
/// </summary>
public sealed class InboundAttachmentInputProcessor
{
    private readonly IAttachmentTextExtractionService _textExtractor;

    /// <summary>
    /// Initializes a new instance of the <see cref="InboundAttachmentInputProcessor" /> class.
    /// </summary>
    /// <param name="textExtractor">The text extractor.</param>
    public InboundAttachmentInputProcessor(IAttachmentTextExtractionService textExtractor)
    {
        _textExtractor = textExtractor;
    }

    /// <summary>
    /// Represents the process async.
    /// </summary>
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

/// <summary>
/// Represents the inbound attachment input.
/// </summary>
public sealed class InboundAttachmentInput
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public string? Id { get; init; }
    /// <summary>
    /// Gets or sets the file name.
    /// </summary>
    public string? FileName { get; init; }
    /// <summary>
    /// Gets or sets the content type.
    /// </summary>
    public string? ContentType { get; init; }
    /// <summary>
    /// Gets or sets the size.
    /// </summary>
    public long? Size { get; init; }
    /// <summary>
    /// Gets or sets the caption.
    /// </summary>
    public string? Caption { get; init; }
    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    public string? Text { get; init; }
    /// <summary>
    /// Gets or sets the base64 content.
    /// </summary>
    public string? Base64Content { get; init; }
}
