using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Archivist.Embedding;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Tests.Unit.Archivist;

public class EmbeddingServiceTests
{
    private static IOptions<LeanKernelConfig> DefaultConfig() => Options.Create(new LeanKernelConfig
    {
        LiteLlm = new LiteLlmConfig
        {
            BaseUrl = "http://localhost:4000",
            ApiKey = "test",
            EmbeddingModel = "embedding-small"
        },
        Qdrant = new QdrantConfig { EmbeddingDimension = 384 }
    });

    private static HttpClient CreateClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://localhost:4000") };

    [Fact]
    public async Task EmbedAsync_ReturnsSingleVector()
    {
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var responseJson = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new { index = 0, embedding }
            }
        });
        var handler = new FakeEmbeddingHandler(responseJson);

        var service = new EmbeddingService(
            CreateClient(handler), DefaultConfig(), NullLogger<EmbeddingService>.Instance);
        var result = await service.EmbedAsync("test text", CancellationToken.None);

        Assert.Equal(3, result.Length);
        Assert.Equal(0.1f, result[0]);
    }

    [Fact]
    public async Task EmbedBatchAsync_ReturnsMultipleVectors()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new { index = 0, embedding = new float[] { 0.1f, 0.2f } },
                new { index = 1, embedding = new float[] { 0.3f, 0.4f } }
            }
        });
        var handler = new FakeEmbeddingHandler(responseJson);

        var service = new EmbeddingService(
            CreateClient(handler), DefaultConfig(), NullLogger<EmbeddingService>.Instance);
        var result = await service.EmbedBatchAsync(["text1", "text2"], CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(0.1f, result[0][0]);
        Assert.Equal(0.3f, result[1][0]);
    }

    [Fact]
    public async Task EmbedBatchAsync_EmptyInput_ReturnsEmpty()
    {
        var handler = new FakeEmbeddingHandler("{}");

        var service = new EmbeddingService(
            CreateClient(handler), DefaultConfig(), NullLogger<EmbeddingService>.Instance);
        var result = await service.EmbedBatchAsync([], CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task EmbedBatchAsync_EmptyResponse_ReturnsZeroVectors()
    {
        var responseJson = JsonSerializer.Serialize(new { data = Array.Empty<object>() });
        var handler = new FakeEmbeddingHandler(responseJson);

        var service = new EmbeddingService(
            CreateClient(handler), DefaultConfig(), NullLogger<EmbeddingService>.Instance);
        var result = await service.EmbedBatchAsync(["text1"], CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(384, result[0].Length); // dimension from config
        Assert.All(result[0], f => Assert.Equal(0f, f));
    }

    [Fact]
    public async Task EmbedBatchAsync_HttpError_ReturnsZeroVectors()
    {
        var handler = new FakeEmbeddingHandler("", statusCode: HttpStatusCode.InternalServerError);

        var service = new EmbeddingService(
            CreateClient(handler), DefaultConfig(), NullLogger<EmbeddingService>.Instance);
        var result = await service.EmbedBatchAsync(["text1", "text2"], CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, v => Assert.Equal(384, v.Length));
    }

    [Fact]
    public async Task EmbedBatchAsync_NetworkError_ReturnsZeroVectors()
    {
        var handler = new FakeEmbeddingHandler(throwException: true);

        var service = new EmbeddingService(
            CreateClient(handler), DefaultConfig(), NullLogger<EmbeddingService>.Instance);
        var result = await service.EmbedBatchAsync(["text1"], CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(384, result[0].Length);
    }

    [Fact]
    public async Task EmbedBatchAsync_OrdersByIndex()
    {
        // Return out-of-order indices to verify sorting
        var responseJson = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new { index = 1, embedding = new float[] { 0.3f } },
                new { index = 0, embedding = new float[] { 0.1f } }
            }
        });
        var handler = new FakeEmbeddingHandler(responseJson);

        var service = new EmbeddingService(
            CreateClient(handler), DefaultConfig(), NullLogger<EmbeddingService>.Instance);
        var result = await service.EmbedBatchAsync(["a", "b"], CancellationToken.None);

        Assert.Equal(0.1f, result[0][0]); // index 0 first
        Assert.Equal(0.3f, result[1][0]); // index 1 second
    }

    private sealed class FakeEmbeddingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;
        private readonly bool _throwException;

        public FakeEmbeddingHandler(string responseBody = "{}", HttpStatusCode statusCode = HttpStatusCode.OK, bool throwException = false)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
            _throwException = throwException;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_throwException) throw new HttpRequestException("Network error");
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
