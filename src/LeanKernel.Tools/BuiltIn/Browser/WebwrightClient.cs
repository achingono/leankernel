using System.Net.Http.Json;
using System.Text.Json;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Tools.BuiltIn.Browser;

/// <summary>
/// HTTP client for the browser automation sidecar.
/// </summary>
public sealed class WebwrightClient : IWebwrightClient
{
    /// <summary>
    /// The named HTTP client used for webwright operational calls.
    /// </summary>
    public const string HttpClientName = "LeanKernel.Webwright";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebwrightClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public WebwrightClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <inheritdoc />
    public Task<BrowserRunSubmissionResponse> SubmitRunAsync(BrowserRunTaskRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SubmitRunCoreAsync(request, ct);
    }

    private async Task<BrowserRunSubmissionResponse> SubmitRunCoreAsync(BrowserRunTaskRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "runs")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        return await SendJsonAsync<BrowserRunSubmissionResponse>(message, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<BrowserRunStatusResponse> GetRunAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return GetRunCoreAsync(runId, ct);
    }

    private async Task<BrowserRunStatusResponse> GetRunCoreAsync(string runId, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"runs/{Uri.EscapeDataString(runId)}");
        return await SendJsonAsync<BrowserRunStatusResponse>(message, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BrowserArtifactContent> GetArtifactAsync(string runId, string artifactId, int maxBytes, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "Maximum artifact bytes must be positive.");
        }

        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"runs/{Uri.EscapeDataString(runId)}/artifacts/{Uri.EscapeDataString(artifactId)}");
        using var response = await CreateClient().SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, ct).ConfigureAwait(false);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var bytes = await ReadBoundedBytesAsync(response.Content, maxBytes + 1, ct).ConfigureAwait(false);
        var truncated = bytes.Length > maxBytes;
        if (!truncated)
        {
            return new BrowserArtifactContent(runId, artifactId, contentType, bytes, Truncated: false);
        }

        if (!IsTextContent(contentType))
        {
            throw new WebwrightException(
                "LIMIT_EXCEEDED",
                $"Artifact '{artifactId}' exceeds the configured limit of {maxBytes} bytes.",
                details: new Dictionary<string, object?> { ["maxBytes"] = maxBytes });
        }

        return new BrowserArtifactContent(runId, artifactId, contentType, bytes[..maxBytes], Truncated: true);
    }

    /// <inheritdoc />
    public async Task<BrowserCancelRunResponse> CancelRunAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        using var message = new HttpRequestMessage(HttpMethod.Delete, $"runs/{Uri.EscapeDataString(runId)}");
        return await SendJsonAsync<BrowserCancelRunResponse>(message, ct).ConfigureAwait(false);
    }

    private async Task<T> SendJsonAsync<T>(HttpRequestMessage message, CancellationToken ct)
    {
        using var response = await CreateClient().SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, ct).ConfigureAwait(false);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        return result ?? throw new WebwrightException("INTERNAL_ERROR", "Browser service returned an empty response.", (int)response.StatusCode);
    }

    private HttpClient CreateClient() => _httpClientFactory.CreateClient(HttpClientName);

    private static async Task<WebwrightException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var content = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                var error = JsonSerializer.Deserialize<WebwrightError>(content, JsonOptions);
                if (error is not null && !string.IsNullOrWhiteSpace(error.Code))
                {
                    return new WebwrightException(error.Code, error.Message, (int)response.StatusCode, error.Details);
                }
            }
            catch (JsonException)
            {
                // Fall through to HTTP status mapping below.
            }
        }

        var code = (int)response.StatusCode switch
        {
            401 or 403 => "UNAUTHORIZED",
            404 => "NOT_FOUND",
            409 => "CONFLICT",
            408 => "TIMEOUT",
            429 => "LIMIT_EXCEEDED",
            503 => "SERVICE_UNAVAILABLE",
            _ => "INTERNAL_ERROR"
        };
        return new WebwrightException(code, $"Browser service returned {(int)response.StatusCode} ({response.ReasonPhrase}).", (int)response.StatusCode);
    }

    private static async Task<byte[]> ReadBoundedBytesAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var memory = new MemoryStream(capacity: Math.Min(maxBytes, 81920));
        var buffer = new byte[81920];

        while (memory.Length < maxBytes)
        {
            var remaining = maxBytes - (int)memory.Length;
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private static bool IsTextContent(string contentType)
        => contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("text/x-python", StringComparison.OrdinalIgnoreCase);
}
