using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools.Dynamic;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.BuiltIn.Internet;

/// <summary>
/// Performs a bounded HTTP request with optional headers, query, and body.
/// </summary>
public static class HttpRequestTool
{
    private const string ToolName = "http_request";
    private const int DefaultMaxRedirects = 3;
    private const int MaxRedirectCeiling = 20;
    private const int DefaultMaxOutputChars = 8_000;
    private const int MaxOutputCharsLimit = 20_000;
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD"
    };

    /// <summary>
    /// Creates a tool definition for performing HTTP requests.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <returns>A <see cref="ToolDefinition"/> for the HTTP request tool.</returns>
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
                new ToolParameter { Name = "follow_redirects", Type = "boolean", Description = "Whether to follow redirects with validation (max hops from Agents:Tools:Internet:MaxRedirects)", Required = false }
            ],
            Handler = async (args, ct) =>
            {
                return await ParseAndExecuteRequestAsync(args, scopeFactory, ct);
            }
        };
    }

    private static async Task<ToolResult> ParseAndExecuteRequestAsync(
        IReadOnlyDictionary<string, object?> args,
        IServiceScopeFactory scopeFactory,
        CancellationToken ct)
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

        var headers = ToolArgumentReader.GetStringDictionary(args, "headers");
        var query = ToolArgumentReader.GetStringDictionary(args, "query");
        var maxOutputChars = Math.Clamp(ToolArgumentReader.GetInt32OrDefault(args, "max_output_chars", DefaultMaxOutputChars), 1, MaxOutputCharsLimit);
        var followRedirects = ToolArgumentReader.GetBoolOrDefault(args, "follow_redirects", true);

        string? bodyContent = null;
        var isJsonBody = false;
        var bodyRaw = ToolArgumentReader.GetJson(args, "body");
        if (!string.IsNullOrWhiteSpace(bodyRaw))
        {
            if (bodyRaw.StartsWith('{') || bodyRaw.StartsWith('['))
            {
                bodyContent = bodyRaw;
                isJsonBody = true;
            }
            else
            {
                bodyContent = bodyRaw;
            }
        }

        var rawContentType = ToolArgumentReader.GetString(args, "content_type");
        var contentType = isJsonBody && string.IsNullOrWhiteSpace(rawContentType) ? "application/json" : rawContentType;

        try
        {
            using var scope = scopeFactory.CreateScope();
            using var client = CreateHttpClient(scope.ServiceProvider);
            var requestUri = AppendQuery(uri!, query);
            var maxRedirects = GetMaxRedirects(scope.ServiceProvider);

            var responseResult = await SendWithRedirectsAsync(
                client,
                requestUri,
                new RequestExecutionOptions(method, headers, bodyContent, contentType, followRedirects, maxRedirects, maxOutputChars),
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
        RequestExecutionOptions options,
        CancellationToken ct)
    {
        var currentUri = initialUri;

        for (var redirectCount = 0; redirectCount <= options.MaxRedirects; redirectCount++)
        {
            using var request = BuildRequest(options.Method, currentUri, options.Headers, options.BodyContent, options.ContentType);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (IsRedirect(response.StatusCode) && options.FollowRedirects)
            {
                if (redirectCount == options.MaxRedirects)
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

            return await BuildOutputAsync(response, options.MaxOutputChars, ct);
        }

        return (false, null, $"Too many redirects while requesting '{initialUri}'.");
    }

    private static int GetMaxRedirects(IServiceProvider serviceProvider)
    {
        var configured = serviceProvider.GetService<IOptions<AgentSettings>>()?.Value.Tools.Internet.MaxRedirects;
        return Math.Clamp(configured ?? DefaultMaxRedirects, 0, MaxRedirectCeiling);
    }

    private sealed record RequestExecutionOptions(
        string Method,
        IReadOnlyDictionary<string, string> Headers,
        string? BodyContent,
        string? ContentType,
        bool FollowRedirects,
        int MaxRedirects,
        int MaxOutputChars);

    private static HttpRequestMessage BuildRequest(
        string method, Uri uri, IReadOnlyDictionary<string, string> headers,
        string? bodyContent, string? contentType)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), uri);
        request.Headers.TryAddWithoutValidation("User-Agent", "LeanKernel/1.0");

        if (!string.IsNullOrEmpty(bodyContent))
        {
            request.Content = new StringContent(bodyContent, Encoding.UTF8);
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            }
        }

        foreach (var (key, value) in headers)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(key, value))
            {
                request.Content?.Headers.TryAddWithoutValidation(key, value);
            }
        }

        return request;
    }

    private static async Task<(bool Success, string? Output, string? Error)> BuildOutputAsync(
        HttpResponseMessage response, int maxOutputChars, CancellationToken ct)
    {
        var content = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(ct);
        var truncated = content.Length > maxOutputChars;
        var boundedContent = truncated ? content[..maxOutputChars] : content;

        var responseHeaders = new SortedDictionary<string, string>(StringComparer.Ordinal);
        AddHeadersToDictionary(response.Headers, responseHeaders);
        AddHeadersToDictionary(response.Content?.Headers, responseHeaders);

        var output = JsonSerializer.Serialize(new
        {
            statusCode = (int)response.StatusCode,
            reasonPhrase = response.ReasonPhrase ?? string.Empty,
            responseHeaders,
            contentType = response.Content?.Headers.ContentType?.ToString() ?? string.Empty,
            content = boundedContent,
            truncated
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        return (true, output, null);
    }

    private static void AddHeadersToDictionary(HttpHeaders? headers, SortedDictionary<string, string> dictionary)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var header in headers)
        {
            if (string.Equals(header.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            dictionary[header.Key] = string.Join(", ", header.Value);
        }
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

        if (EgressValidator.IsPrivateOrLoopbackHost(parsed.Host))
        {
            error = "Private, loopback, or link-local URLs are not allowed";
            return false;
        }

        uri = parsed;
        return true;
    }
}