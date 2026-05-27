using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools.BuiltIn.Internet;

/// <summary>
/// Built-in tool: fetches web content from a URL.
/// </summary>
public static class WebFetchTool
{
    private const string ToolName = "web_fetch";
    private const int MaxRedirects = 3;

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
                    var config = scope.ServiceProvider.GetRequiredService<IOptions<LeanKernel.Abstractions.Configuration.LeanKernelConfig>>().Value.FileSystem;
                    var validatedUri = uri ?? throw new InvalidOperationException("URL validation failed.");

                    var result = await FetchAsync(client, validatedUri, config, ct);
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
    private static async Task<(bool Success, string? Output, string? Error)> FetchAsync(HttpClient client, Uri uri, LeanKernel.Abstractions.Configuration.FileSystemConfig config, CancellationToken ct)
    {
        for (var redirectCount = 0; redirectCount <= MaxRedirects; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", "LeanKernel/1.0");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount == MaxRedirects)
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
            var scratchPath = FileSystemSupport.EnsureScratchPath(config, extension);
            try
            {
                var downloadedBytes = await DownloadAsync(response, scratchPath, config.MaxDownloadBytes, ct);
                if (!downloadedBytes)
                {
                    return (false, null, $"Downloaded content exceeded the configured limit of {config.MaxDownloadBytes} bytes.");
                }

                var output = await TextExtractionHelper.ExtractAsync(scratchPath, config, ct);
                return (true, string.IsNullOrWhiteSpace(output) ? "No text could be extracted from the downloaded content." : output, null);
            }
            finally
            {
                TryDeleteScratchFile(scratchPath);
            }
        }

        return (false, null, $"Too many redirects while fetching '{uri}'.");
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
                "image/jpeg" => ".jpg",
                "image/jpg" => ".jpg",
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
            // Best-effort cleanup; delete failures are non-fatal.
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

        if (string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            error = "Localhost URLs are not allowed";
            return false;
        }

        if (IPAddress.TryParse(parsed.Host, out var ipAddress) && IsPrivateOrLoopbackIp(ipAddress))
        {
            error = "Private or loopback IP URLs are not allowed";
            return false;
        }

        uri = parsed;
        return true;
    }

    private static bool IsPrivateOrLoopbackIp(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10)
            {
                return true;
            }

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // 169.254.0.0/16
            return bytes[0] == 169 && bytes[1] == 254;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            // fc00::/7 (unique local) and fe80::/10 (link local)
            return (bytes[0] & 0xFE) == 0xFC || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
        }

        return false;
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

        return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
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