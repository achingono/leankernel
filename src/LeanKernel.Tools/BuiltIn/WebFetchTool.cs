using System.Net;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Tools.BuiltIn;

/// <summary>
/// Built-in tool: fetches web content from a URL.
/// </summary>
public static class WebFetchTool
{
    private const string ToolName = "web_fetch";
    private const int MaxOutputLength = 20_000;

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

                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.TryAddWithoutValidation("User-Agent", "LeanKernel/1.0");

                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        return new ToolResult
                        {
                            ToolName = ToolName,
                            Success = false,
                            Error = $"Web fetch request failed with status {(int)response.StatusCode} ({response.ReasonPhrase})."
                        };
                    }

                    var mediaType = response.Content.Headers.ContentType?.MediaType;
                    if (!IsTextLikeMediaType(mediaType))
                    {
                        return new ToolResult
                        {
                            ToolName = ToolName,
                            Success = false,
                            Error = $"Unsupported content type '{mediaType ?? "unknown"}'."
                        };
                    }

                    var content = await response.Content.ReadAsStringAsync(ct);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        return new ToolResult
                        {
                            ToolName = ToolName,
                            Success = true,
                            Output = "No content found at the provided URL."
                        };
                    }

                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = true,
                        Output = Truncate(content)
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

    private static string Truncate(string content)
    {
        if (content.Length <= MaxOutputLength)
        {
            return content;
        }

        return content[..MaxOutputLength] + "\n\n[Content truncated to 20000 characters.]";
    }
}