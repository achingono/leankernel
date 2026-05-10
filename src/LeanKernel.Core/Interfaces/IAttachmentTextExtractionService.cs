namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Extracts text from inbound message attachments.
/// </summary>
public interface IAttachmentTextExtractionService
{
    /// <summary>
    /// Determines whether text extraction is supported for the attachment metadata.
    /// </summary>
    /// <param name="contentType">The attachment MIME type, if supplied.</param>
    /// <param name="fileName">The attachment file name, if supplied.</param>
    /// <returns><see langword="true" /> when the attachment can be processed; otherwise <see langword="false" />.</returns>
    bool CanExtractText(string? contentType, string? fileName);

    /// <summary>
    /// Extracts text from an attachment payload.
    /// </summary>
    /// <param name="contentType">The attachment MIME type, if supplied.</param>
    /// <param name="fileName">The attachment file name, if supplied.</param>
    /// <param name="bytes">The raw attachment bytes.</param>
    /// <param name="ct">A token used to cancel extraction.</param>
    /// <returns>The extracted text, or <see langword="null" /> when extraction produced no text.</returns>
    Task<string?> ExtractTextAsync(
        string? contentType,
        string? fileName,
        byte[] bytes,
        CancellationToken ct);
}
