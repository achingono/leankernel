using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LeanKernel.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeanKernel.Tests.Unit.Knowledge;

public class GBrainMcpClientTests
{
    [Fact]
    public async Task CallToolAsync_posts_json_rpc_request_and_unwraps_structured_content()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new
            {
                content = Array.Empty<object>(),
                structuredContent = new { value = "ok" },
                isError = false
            }
        }));
        var client = CreateClient(handler);

        var result = await client.CallToolAsync("search", new { query = "atlas" });

        result.Should().NotBeNull();
        result!.Value.GetProperty("value").GetString().Should().Be("ok");

        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        document.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        document.RootElement.GetProperty("method").GetString().Should().Be("tools/call");
        document.RootElement.GetProperty("params").GetProperty("name").GetString().Should().Be("search");
        document.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("query").GetString().Should().Be("atlas");
    }

    [Fact]
    public async Task CallToolAsync_parses_json_from_text_content_when_structured_content_is_missing()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new
            {
                content = new[] { new { type = "text", text = "{\"answer\":42}" } },
                isError = false
            }
        }));
        var client = CreateClient(handler);

        var result = await client.CallToolAsync("search");

        result.Should().NotBeNull();
        result!.Value.GetProperty("answer").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task CallToolAsync_wraps_plain_text_content_as_a_json_string()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new
            {
                content = new[] { new { type = "text", text = "plain text" } },
                isError = false
            }
        }));
        var client = CreateClient(handler);

        var result = await client.CallToolAsync("search");

        result.Should().NotBeNull();
        result!.Value.ValueKind.Should().Be(JsonValueKind.String);
        result.Value.GetString().Should().Be("plain text");
    }

    [Fact]
    public async Task CallToolAsync_throws_when_the_server_returns_an_error()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new
        {
            jsonrpc = "2.0",
            id = 1,
            error = new { code = 500, message = "boom" }
        }));
        var client = CreateClient(handler);

        var act = () => client.CallToolAsync("search");

        var exception = await act.Should().ThrowAsync<GBrainException>();
        exception.Which.ErrorCode.Should().Be(500);
        exception.Which.Message.Should().Be("boom");
    }

    [Fact]
    public async Task ListToolsAsync_posts_list_request_and_maps_tools()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new
            {
                tools = new[]
                {
                    new { name = "search", description = "Search wiki" },
                    new { name = "get_page", description = "Read page" }
                }
            }
        }));
        var client = CreateClient(handler);

        var tools = await client.ListToolsAsync();

        tools.Select(tool => tool.Name).Should().Equal("search", "get_page");
        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        document.RootElement.GetProperty("method").GetString().Should().Be("tools/list");
    }

    private static GBrainMcpClient CreateClient(HttpMessageHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") }, NullLogger<GBrainMcpClient>.Instance);

    private static HttpResponseMessage CreateJsonResponse(object payload)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync().ConfigureAwait(false));
            return _handler(request, cancellationToken);
        }
    }
}
