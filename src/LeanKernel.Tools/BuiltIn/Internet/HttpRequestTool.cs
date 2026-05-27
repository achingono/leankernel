using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Tools.BuiltIn.Internet;

/// <summary>
/// Built-in tool: performs a bounded HTTP request.
/// </summary>
public static class HttpRequestTool
{
    private const string ToolName = "http_request";
    private const int MaxRedirects = 3;
    private const int DefaultMaxOutputChars = 8_000;
    private const int MaxOutputCharsLimit = 20_000;
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET",
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
        "HEAD"
    };

    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Perform a bounded HTTP request with optional headers, query, and body",
            Category = "internet",
            Parameters =
            [
                new ToolParameter { Name = "url", Type = "string", Description = "Absolute HTTP/HTTPS URL to request", Required = true },
                new ToolParameter { Name = "method", Type = "string", Description = "HTTP method (GET|POST|PUT|PATCH|DELETE|HEAD)", Required = false },
                new ToolParameter { Name = "headers", Type = "object", Description = "Request headers as key/value string map", Required = false },
                new ToolParameter { Name = "query", Type = "object", Description = "Query string parameters as key/value string map", Required = false },
                new ToolParameter { Name = "body", Type = "object", Description = "Request body as string or JSON object", Required = false },
                new ToolParameter { Name = "content_type", Type = "string", Description = "Content-Type for request body", Required = false },
                new ToolParameter { Name = "max_output_chars", Type = "integer", Description = "Maximum response characters to return (max 20000)", Required = false },
                new ToolParameter { Name = "follow_redirects", Type = "boolean", Description = "Whether to follow redirects with validation (max 3 hops)", Required = false }
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

                var method = ToolArgumentReader.GetString(args, "method");
                if (string.IsNullOrWhiteSpace(method))
                {
                    method = "GET";
                }

                method = method.Trim().ToUpperInvariant();
                if (!AllowedMethods.Contains(method))
                {
                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = false,
                        Error = "Method must be one of: GET, POST, PUT, PATCH, DELETE, HEAD"
                    };
                }

                if (!TryReadStringMap(args, "headers", out var headers, out var headersError))
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = headersError };
                }

                if (!TryReadStringMap(args, "query", out var query, out var queryError))
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = queryError };
                }

                var maxOutputChars = Math.Clamp(ToolArgumentReader.GetInt32OrDefault(args, "max_output_chars", DefaultMaxOutputChars), 1, MaxOutputCharsLimit);
                var followRedirects = ToolArgumentReader.GetBoolOrDefault(args, "follow_redirects", true);
                var contentType = ToolArgumentReader.GetString(args, "content_type");

                if (!TryReadBody(args, out var bodyContent, out var isJsonBody, out var bodyError))
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = bodyError };
                }

                if (isJsonBody && string.IsNullOrWhiteSpace(contentType))
                {
                    contentType = "application/json";
                }

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    using var client = CreateHttpClient(scope.ServiceProvider);
                    var requestUri = AppendQuery(uri!, query);

                    var responseResult = await SendWithRedirectsAsync(
                        client,
                        requestUri,
                        method,
                        headers,
                        bodyContent,
                        contentType,
                        followRedirects,
                        maxOutputChars,
                        ct);

                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = responseResult.Success,
                        Output = responseResult.Output,
                        Error = responseResult.Error
                    };
                }
                catch (Exception ex)
                {
                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = false,
                        Error = $"HTTP request failed: {ex.Message}"
                    };
                }
            }
        };
    }

    private static HttpClient CreateHttpClient(IServiceProvider serviceProvider)
    {
        var overrideHandler = serviceProvider.GetService<HttpMessageHandler>();
        if (overrideHandler is not null)
        {
            return new HttpClient(overrideHandler, disposeHandler: false);
        }

        return new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
    }

    private static async Task<(bool Success, string? Output, string? Error)> SendWithRedirectsAsync(
        HttpClient client,
        Uri initialUri,
        string method,
        IReadOnlyDictionary<string, string> headers,
        string? bodyContent,
        string? contentType,
        bool followRedirects,
        int maxOutputChars,
        CancellationToken ct)
    {
        var currentUri = initialUri;

        for (var redirectCount = 0; redirectCount <= MaxRedirects; redirectCount++)
        {
            using var request = BuildRequest(method, currentUri, headers, bodyContent, contentType);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (IsRedirect(response.StatusCode) && followRedirects)
            {
                if (redirectCount == MaxRedirects)
                {
                    return (false, null, $"Too many redirects while requesting '{currentUri}'.");
                }

                var location = response.Headers.Location;
                if (location is null)
                {
                    return (false, null, $"Redirect response from '{currentUri}' did not include a Location header.");
                }

                var nextUri = new Uri(currentUri, location);
                if (!TryValidateUrl(nextUri.ToString(), out var validatedUri, out var validationError))
                {
                    return (false, null, validationError);
                }

                currentUri = validatedUri!;
                continue;
            }

            return await BuildOutputAsync(response, maxOutputChars, ct);
        }

        return (false, null, $"Too many redirects while requesting '{initialUri}'.");
    }

    private static HttpRequestMessage BuildRequest(
        string method,
        Uri uri,
        IReadOnlyDictionary<string, string> headers,
        string? bodyContent,
        string? contentType)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), uri);
        request.Headers.TryAddWithoutValidation("User-Agent", "LeanKernel/1.0");

        if (!string.IsNullOrEmpty(bodyContent))
        {
            request.Content = new StringContent(bodyContent, Encoding.UTF8);
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
            }
        }

        foreach (var (key, value) in headers)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Header names must be non-empty strings.");
            }

            if (!request.Headers.TryAddWithoutValidation(key, value))
            {
                if (request.Content is null || !request.Content.Headers.TryAddWithoutValidation(key, value))
                {
                    throw new InvalidOperationException($"Invalid header '{key}'.");
                }
            }
        }

        return request;
    }

    private static async Task<(bool Success, string? Output, string? Error)> BuildOutputAsync(
        HttpResponseMessage response,
        int maxOutputChars,
        CancellationToken ct)
    {
        var content = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(ct);
        var truncated = content.Length > maxOutputChars;
        var boundedContent = truncated ? content[..maxOutputChars] : content;

        var responseHeaders = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var header in response.Headers)
        {
            if (string.Equals(header.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            responseHeaders[header.Key] = string.Join(", ", header.Value);
        }

        if (response.Content is not null)
        {
            foreach (var header in response.Content.Headers)
            {
                if (string.Equals(header.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                responseHeaders[header.Key] = string.Join(", ", header.Value);
            }
        }

        var output = JsonSerializer.Serialize(new HttpRequestOutput(
            StatusCode: (int)response.StatusCode,
            ReasonPhrase: response.ReasonPhrase ?? string.Empty,
            ResponseHeaders: responseHeaders,
            ContentType: response.Content?.Headers.ContentType?.ToString() ?? string.Empty,
            Content: boundedContent,
            Truncated: truncated), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

        return (true, output, null);
    }

    private static bool TryReadStringMap(
        IDictionary<string, object?> arguments,
        string name,
        out IReadOnlyDictionary<string, string> map,
        out string? error)
    {
        map = new Dictionary<string, string>(StringComparer.Ordinal);
        error = null;

        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return true;
        }

        if (value is JsonElement element)
        {
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return true;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                error = $"'{name}' must be an object of string key/value pairs.";
                return false;
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    error = $"'{name}' values must be strings, numbers, booleans, or null.";
                    return false;
                }

                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Null => string.Empty,
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    _ => property.Value.ToString()
                };
            }

            map = result;
            return true;
        }

        if (value is IDictionary<string, string> typedStringDictionary)
        {
            map = new Dictionary<string, string>(typedStringDictionary, StringComparer.Ordinal);
            return true;
        }

        if (value is IDictionary<string, object?> objectDictionary)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, item) in objectDictionary)
            {
                if (item is null)
                {
                    result[key] = string.Empty;
                    continue;
                }

                if (item is string text)
                {
                    result[key] = text;
                    continue;
                }

                if (item is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
                {
                    result[key] = Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                    continue;
                }

                error = $"'{name}' values must be strings, numbers, booleans, or null.";
                return false;
            }

            map = result;
            return true;
        }

        error = $"'{name}' must be an object of string key/value pairs.";
        return false;
    }

    private static bool TryReadBody(
        IDictionary<string, object?> arguments,
        out string? bodyContent,
        out bool isJsonBody,
        out string? error)
    {
        bodyContent = null;
        isJsonBody = false;
        error = null;

        if (!arguments.TryGetValue("body", out var value) || value is null)
        {
            return true;
        }

        if (value is string text)
        {
            bodyContent = text;
            return true;
        }

        if (value is JsonElement element)
        {
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                bodyContent = element.GetString();
                return true;
            }

            if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                bodyContent = element.GetRawText();
                isJsonBody = true;
                return true;
            }

            error = "Body must be a string, object, or array.";
            return false;
        }

        if (value is IDictionary<string, object?> or IEnumerable<object>)
        {
            bodyContent = JsonSerializer.Serialize(value);
            isJsonBody = true;
            return true;
        }

        error = "Body must be a string, object, or array.";
        return false;
    }

    private static Uri AppendQuery(Uri uri, IReadOnlyDictionary<string, string> query)
    {
        if (query.Count == 0)
        {
            return uri;
        }

        var builder = new UriBuilder(uri);
        var queryBuilder = new StringBuilder();

        if (!string.IsNullOrEmpty(builder.Query))
        {
            queryBuilder.Append(builder.Query.TrimStart('?'));
        }

        foreach (var (key, value) in query)
        {
            if (queryBuilder.Length > 0)
            {
                queryBuilder.Append('&');
            }

            queryBuilder.Append(Uri.EscapeDataString(key));
            queryBuilder.Append('=');
            queryBuilder.Append(Uri.EscapeDataString(value));
        }

        builder.Query = queryBuilder.ToString();
        return builder.Uri;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is >= 300 and <= 399;
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
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            if (bytes[0] == 10)
            {
                return true;
            }

            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            return bytes[0] == 169 && bytes[1] == 254;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return (bytes[0] & 0xFE) == 0xFC || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
        }

        return false;
    }

    private sealed record HttpRequestOutput(
        int StatusCode,
        string ReasonPhrase,
        IReadOnlyDictionary<string, string> ResponseHeaders,
        string ContentType,
        string Content,
        bool Truncated);
}
