using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Host.Services;

public sealed class AttachmentTextExtractionService : IAttachmentTextExtractionService
{
    private const int MaxExtractedCharacters = 12_000;

    private static readonly HashSet<string> DocumentMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/rtf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/epub+zip",
        "message/rfc822"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".doc",
        ".docx",
        ".ppt",
        ".pptx",
        ".xls",
        ".xlsx",
        ".rtf",
        ".odt",
        ".epub",
        ".eml",
        ".msg"
    };

    private readonly HttpClient _httpClient;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<AttachmentTextExtractionService> _logger;

    public AttachmentTextExtractionService(
        HttpClient httpClient,
        IOptions<LeanKernelConfig> config,
        ILogger<AttachmentTextExtractionService> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
    }

    public bool CanExtractText(string? contentType, string? fileName)
    {
        if (InboundAttachmentTextExtractor.CanExtractText(contentType, fileName))
            return true;

        if (!string.IsNullOrWhiteSpace(contentType) && DocumentMimeTypes.Contains(contentType))
            return true;

        var extension = Path.GetExtension(fileName ?? string.Empty);
        return !string.IsNullOrWhiteSpace(extension) && DocumentExtensions.Contains(extension);
    }

    public async Task<string?> ExtractTextAsync(
        string? contentType,
        string? fileName,
        byte[] bytes,
        CancellationToken ct)
    {
        if (bytes.Length == 0 || !CanExtractText(contentType, fileName))
            return null;

        var directText = InboundAttachmentTextExtractor.TryExtractText(contentType, fileName, bytes);
        if (!string.IsNullOrWhiteSpace(directText))
            return directText;

        if (!_config.Unstructured.Enabled)
            return null;

        try
        {
            using var form = new MultipartFormDataContent();
            using var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType ?? "application/octet-stream");

            form.Add(fileContent, "files", fileName ?? "attachment");
            form.Add(new StringContent("auto"), "strategy");
            form.Add(new StringContent("by_title"), "chunking_strategy");
            form.Add(new StringContent("4000"), "max_characters");

            using var response = await _httpClient.PostAsync("/general/v0/general", form, ct);
            response.EnsureSuccessStatusCode();

            var elements = await response.Content.ReadFromJsonAsync<List<UnstructuredElement>>(cancellationToken: ct);
            if (elements is null || elements.Count == 0)
                return null;

            var text = string.Join(
                "\n\n",
                elements
                    .Select(element => element.Text?.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

            text = InboundAttachmentTextExtractor.NormalizeText(text);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            return text.Length <= MaxExtractedCharacters
                ? text
                : text[..MaxExtractedCharacters] + "\n...[truncated]";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to extract attachment text for {FileName} via unstructured",
                fileName ?? "attachment");
            return null;
        }
    }

    private sealed record UnstructuredElement(string? Text);
}
