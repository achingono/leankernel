using System.Net;
using System.Text;
using FluentAssertions;
using LeanKernel.Gateway.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeanKernel.Tests.Unit.Providers;

/// <summary>
/// Covers parsing and error handling for the GBrain MCP client.
/// </summary>
public class GBrainMcpClientTests
{
    /// <summary>
    /// Verifies JSON RPC success payloads are returned as structured results.
    /// </summary>
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

    /// <summary>
    /// Verifies MCP errors are surfaced as client exceptions.
    /// </summary>
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

    /// <summary>
    /// Verifies server-sent event payloads are parsed correctly.
    /// </summary>
    [Fact]
    public async Task CallToolAsync_ParsesSseTransport()
    {
        var sse = "data: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"value\":7}}\n\n";
        var client = CreateClient(new StubHandler(HttpStatusCode.OK, "text/event-stream", sse));

        var result = await client.CallToolAsync("search");

        result.Should().NotBeNull();
        result!.Value.GetProperty("value").GetInt32().Should().Be(7);
    }

    /// <summary>
    /// Verifies tool envelopes containing JSON text are unwrapped.
    /// </summary>
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

    /// <summary>
    /// Verifies error tool envelopes are surfaced as client exceptions.
    /// </summary>
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

    /// <summary>
    /// Creates a client backed by the supplied HTTP handler.
    /// </summary>
    private static GBrainMcpClient CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/mcp") };
        return new GBrainMcpClient(http, NullLogger<GBrainMcpClient>.Instance);
    }

    /// <summary>
    /// Returns a fixed HTTP response for MCP client tests.
    /// </summary>
    /// <param name="code">The status code to return.</param>
    /// <param name="mediaType">The content media type to return.</param>
    /// <param name="content">The response content to return.</param>
    private sealed class StubHandler(HttpStatusCode code, string mediaType, string content) : HttpMessageHandler
    {
        /// <inheritdoc />
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
