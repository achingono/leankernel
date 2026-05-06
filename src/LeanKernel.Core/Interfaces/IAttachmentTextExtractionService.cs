namespace LeanKernel.Core.Interfaces;

public interface IAttachmentTextExtractionService
{
    bool CanExtractText(string? contentType, string? fileName);

    Task<string?> ExtractTextAsync(
        string? contentType,
        string? fileName,
        byte[] bytes,
        CancellationToken ct);
}
