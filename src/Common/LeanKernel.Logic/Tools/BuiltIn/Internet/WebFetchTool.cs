using System.Diagnostics.CodeAnalysis;
using System.Net;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools.Dynamic;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.BuiltIn.Internet;

/// <summary>
/// Fetches web content from a URL with SSRF protection and text extraction.
/// </summary>
public static class WebFetchTool
{
    private const string ToolName = "web_fetch";
    private const int DefaultMaxRedirects = 3;
    private const int MaxRedirectCeiling = 20;

    /// <summary>
    /// Creates a tool definition for fetching web content.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <returns>A <see cref="ToolDefinition"/> for the web fetch tool.</returns>
    [SuppressMessage("Major Code Smell", "S3776", Justification = "Fetch handler stays explicit to preserve SSRF and download safety checks.")]
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Fetch content from a web URL",
            Category = "internet",
            Parameters =
            [
                new ToolParameter { Name = "url", Type = "string", Description = "Absolute HTTP/HTTPS URL to fetch", Required = true }
            ],
            Handler = async (args, ct) =>
            {
                var url = ToolArgumentReader.GetString(args, "url");

                if (string.IsNullOrWhiteSpace(url))
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = "URL is required" };
                }

                if (!TryValidateUrl(url, out var uri, out var validationError))
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = validationError };
                }

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var client = scope.ServiceProvider.GetRequiredService<HttpClient>();
                    var fileSettings = scope.ServiceProvider.GetRequiredService<IOptions<FileSettings>>().Value;
                    var maxRedirects = GetMaxRedirects(scope.ServiceProvider);
                    var validatedUri = uri ?? throw new InvalidOperationException("URL validation failed.");

                    var result = await FetchAsync(client, validatedUri, fileSettings, maxRedirects, ct);
                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = result.Success,
                        Output = result.Output,
                        Error = result.Error
                    };
                }
                catch (Exception ex)
                {
                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = false,
                        Error = $"Web fetch failed: {ex.Message}"
                    };
                }
            }
        };
    }

    [SuppressMessage("Major Code Smell", "S3776", Justification = "Redirect and content handling is intentionally explicit for safety.")]
    private static async Task<(bool Success, string? Output, string? Error)> FetchAsync(HttpClient client, Uri uri, FileSettings config, int maxRedirects, CancellationToken ct)
    {
        for (var redirectCount = 0; redirectCount <= maxRedirects; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", "LeanKernel/1.0");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount == maxRedirects)
                {
                    return (false, null, $"Too many redirects while fetching '{uri}'.");
                }

                var location = response.Headers.Location;
                if (location is null)
                {
                    return (false, null, $"Redirect response from '{uri}' did not include a Location header.");
                }

                if (!TryValidateUrl(new Uri(uri, location).ToString(), out var nextUri, out var validationError))
                {
                    return (false, null, validationError);
                }

                uri = nextUri!;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                return (false, null, $"Web fetch request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (IsTextLikeMediaType(mediaType))
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                if (string.IsNullOrWhiteSpace(content))
                {
                    return (true, "No content found at the provided URL.", null);
                }

                return (true, Truncate(content, config.MaxExtractedCharacters), null);
            }

            var extension = GetExtension(mediaType, uri);
            var scratchRoot = string.IsNullOrWhiteSpace(config.ScratchRoot) ? Path.GetTempPath() : config.ScratchRoot;
            var scratchPath = FileSystemSupport.EnsureScratchPath(scratchRoot, extension);
            try
            {
                var downloadedBytes = await DownloadAsync(response, scratchPath, config.MaxDownloadBytes, ct);
                if (!downloadedBytes)
                {
                    return (false, null, $"Downloaded content exceeded the configured limit of {config.MaxDownloadBytes} bytes.");
                }

                var output = await TextExtractionHelper.ExtractAsync(scratchPath, config.ScratchRoot, config.PythonExecutable, config.MaxExtractedCharacters, ct);
                return (true, string.IsNullOrWhiteSpace(output) ? "No text could be extracted from the downloaded content." : output, null);
            }
            finally
            {
                TryDeleteScratchFile(scratchPath);
            }
        }

        return (false, null, $"Too many redirects while fetching '{uri}'.");
    }

    private static int GetMaxRedirects(IServiceProvider serviceProvider)
    {
        var configured = serviceProvider.GetService<IOptions<AgentSettings>>()?.Value.Tools.Internet.MaxRedirects;
        return Math.Clamp(configured ?? DefaultMaxRedirects, 0, MaxRedirectCeiling);
    }

    private static async Task<bool> DownloadAsync(HttpResponseMessage response, string destinationPath, long maxBytes, CancellationToken ct)
    {
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(destinationPath);
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                target.Close();
                TryDeleteScratchFile(destinationPath);
                return false;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return true;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is >= 300 and <= 399;
    }

    private static string GetExtension(string? mediaType, Uri uri)
    {
        var urlExtension = Path.GetExtension(uri.AbsolutePath);

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            return mediaType.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/jpeg" or "image/jpg" => ".jpg",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "image/bmp" => ".bmp",
                "image/tiff" => ".tiff",
                "application/pdf" => ".pdf",
                "application/octet-stream" => string.IsNullOrWhiteSpace(urlExtension) ? ".bin" : urlExtension,
                _ => string.IsNullOrWhiteSpace(urlExtension) ? ".bin" : urlExtension
            };
        }

        return string.IsNullOrWhiteSpace(urlExtension) ? ".bin" : urlExtension;
    }

    private static void TryDeleteScratchFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static bool TryValidateUrl(string url, out Uri? uri, out string? error)
    {
        uri = null;
        error = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            error = "URL must be an absolute HTTP or HTTPS URL";
            return false;
        }

        if (EgressValidator.IsPrivateOrLoopbackHost(parsed.Host))
        {
            error = "Private, loopback, or link-local URLs are not allowed";
            return false;
        }

        uri = parsed;
        return true;
    }

    private static bool IsTextLikeMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return true;
        }

        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return mediaType.Equals(Constants.ContentTypes.Json, StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string content, int maxLength)
    {
        if (content.Length <= maxLength)
        {
            return content;
        }

        return content[..maxLength] + $"\n\n[Content truncated to {maxLength} characters.]";
    }
}