using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// Retrieves embedding vectors from a configured OpenAI-compatible embeddings endpoint.
/// </summary>
public sealed class HttpEmbeddingClient(
    HttpClient httpClient,
    IOptions<EmbeddingClientSettings> embeddingSettings,
    ILogger<HttpEmbeddingClient> logger) : IEmbeddingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly EmbeddingClientSettings _settings = embeddingSettings.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GetEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        string model,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
        {
            return [];
        }

        try
        {
            if (string.IsNullOrWhiteSpace(_settings.Endpoint))
            {
                logger.LogWarning("Embedding endpoint is not configured; skipping embedding call.");
                return [];
            }

            var request = new EmbeddingRequest(Model: model, Input: inputs);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _settings.Endpoint)
            {
                Content = JsonContent.Create(request, options: JsonOptions),
            };

            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    _settings.AuthScheme,
                    _settings.ApiKey);
            }

            using var response = await httpClient
                .SendAsync(httpRequest, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var payload = await response.Content
                .ReadFromJsonAsync<EmbeddingResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (payload?.Data is null || payload.Data.Count == 0)
            {
                logger.LogWarning("Embedding endpoint returned no data for {Count} inputs.", inputs.Count);
                return [];
            }

            return payload.Data
                .OrderBy(d => d.Index)
                .Select(d => (ReadOnlyMemory<float>)d.Embedding)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedding call failed for {Count} inputs.", inputs.Count);
            return [];
        }
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] List<EmbeddingData> Data);

    private sealed record EmbeddingData(
        [property: JsonPropertyName("embedding")] float[] Embedding,
        [property: JsonPropertyName("index")] int Index);
}
