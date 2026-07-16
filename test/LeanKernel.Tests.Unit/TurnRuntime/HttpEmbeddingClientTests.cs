using System.Net;
using System.Text;
using FluentAssertions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.TurnRuntime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LeanKernel.Tests.Unit.TurnRuntime;

public class HttpEmbeddingClientTests
{
    [Fact]
    public async Task GetEmbeddingsAsync_returns_empty_when_inputs_are_empty()
    {
        var client = CreateClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called")), new EmbeddingClientSettings
        {
            Endpoint = "https://example.test/embeddings"
        });

        var result = await client.GetEmbeddingsAsync([], "text-embedding-3-small", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEmbeddingsAsync_returns_empty_when_endpoint_is_missing()
    {
        var client = CreateClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called")), new EmbeddingClientSettings());

        var result = await client.GetEmbeddingsAsync(["alpha"], "text-embedding-3-small", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEmbeddingsAsync_orders_vectors_by_index_and_sets_auth_header()
    {
        HttpRequestMessage? capturedRequest = null;
        var payload = """
        {
          "data": [
            { "embedding": [0.4, 0.5], "index": 1 },
            { "embedding": [0.1, 0.2], "index": 0 }
          ]
        }
        """;

        var client = CreateClient(new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }), new EmbeddingClientSettings
        {
            Endpoint = "https://example.test/embeddings",
            ApiKey = "abc123",
            AuthScheme = "Bearer"
        });

        var result = await client.GetEmbeddingsAsync(["one", "two"], "text-embedding-3-small", CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].ToArray().Should().Equal(0.1f, 0.2f);
        result[1].ToArray().Should().Equal(0.4f, 0.5f);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.AbsoluteUri.Should().Be("https://example.test/embeddings");
        capturedRequest.Headers.Authorization.Should().NotBeNull();
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be("abc123");
    }

    [Fact]
    public async Task GetEmbeddingsAsync_returns_empty_when_response_has_no_data()
    {
        var client = CreateClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[]}", Encoding.UTF8, "application/json")
        }), new EmbeddingClientSettings
        {
            Endpoint = "https://example.test/embeddings"
        });

        var result = await client.GetEmbeddingsAsync(["one"], "text-embedding-3-small", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEmbeddingsAsync_returns_empty_when_http_request_fails()
    {
        var client = CreateClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)), new EmbeddingClientSettings
        {
            Endpoint = "https://example.test/embeddings"
        });

        var result = await client.GetEmbeddingsAsync(["one"], "text-embedding-3-small", CancellationToken.None);

        result.Should().BeEmpty();
    }

    private static HttpEmbeddingClient CreateClient(HttpMessageHandler handler, EmbeddingClientSettings settings)
    {
        var httpClient = new HttpClient(handler);
        return new HttpEmbeddingClient(httpClient, Options.Create(settings), NullLogger<HttpEmbeddingClient>.Instance);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
