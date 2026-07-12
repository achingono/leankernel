using System.Net;
using System.Text;
using FluentAssertions;
using LeanKernel.Gateway.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeanKernel.Tests.Unit.Providers;

public class GBrainMcpClientTests
{
    [Fact]
    public async Task CallToolAsync_ReturnsStructuredResult_ForJsonResponse()
    {
        var json = """
        {"jsonrpc":"2.0","id":1,"result":{"value":42}}
        """;
        var client = CreateClient(new StubHandler(HttpStatusCode.OK, "application/json", json));

        var result = await client.CallToolAsync("search", new { query = "q" });

        result.Should().NotBeNull();
        result!.Value.GetProperty("value").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task CallToolAsync_Throws_OnMcpError()
    {
        var json = """
        {"jsonrpc":"2.0","id":1,"error":{"code":500,"message":"boom"}}
        """;
        var client = CreateClient(new StubHandler(HttpStatusCode.OK, "application/json", json));

        var act = () => client.CallToolAsync("search");
        await act.Should().ThrowAsync<GBrainException>().WithMessage("boom");
    }

    [Fact]
    public async Task CallToolAsync_ParsesSseTransport()
    {
        var sse = "data: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"value\":7}}\n\n";
        var client = CreateClient(new StubHandler(HttpStatusCode.OK, "text/event-stream", sse));

        var result = await client.CallToolAsync("search");

        result.Should().NotBeNull();
        result!.Value.GetProperty("value").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task CallToolAsync_UnwrapsToolEnvelopeAndParsesTextJson()
    {
        var json = """
        {
          "jsonrpc":"2.0",
          "id":1,
          "result":{
            "content":[{"type":"text","text":"{\"score\":0.9}"}],
            "isError":false
          }
        }
        """;
        var client = CreateClient(new StubHandler(HttpStatusCode.OK, "application/json", json));

        var result = await client.CallToolAsync("search");

        result.Should().NotBeNull();
        result!.Value.GetProperty("score").GetDouble().Should().Be(0.9);
    }

    [Fact]
    public async Task CallToolAsync_Throws_WhenToolEnvelopeIsError()
    {
        var json = """
        {
          "jsonrpc":"2.0",
          "id":1,
          "result":{
            "content":[{"type":"text","text":"bad request"}],
            "isError":true
          }
        }
        """;
        var client = CreateClient(new StubHandler(HttpStatusCode.OK, "application/json", json));

        var act = () => client.CallToolAsync("search");
        await act.Should().ThrowAsync<GBrainException>().WithMessage("bad request");
    }

    private static GBrainMcpClient CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/mcp") };
        return new GBrainMcpClient(http, NullLogger<GBrainMcpClient>.Instance);
    }

    private sealed class StubHandler(HttpStatusCode code, string mediaType, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(code)
            {
                Content = new StringContent(content, Encoding.UTF8, mediaType)
            };

            return Task.FromResult(response);
        }
    }
}
