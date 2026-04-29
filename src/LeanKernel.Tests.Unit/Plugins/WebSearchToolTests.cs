using System.Net;
using System.Text.Json;
using LeanKernel.Plugins.BuiltIn;
using Xunit;

namespace LeanKernel.Tests.Unit.Plugins;

public class WebSearchToolTests
{
    [Fact]
    public void Name_IsWebSearch()
    {
        var tool = new WebSearchTool(new HttpClient());
        Assert.Equal("web_search", tool.Name);
    }

    [Fact]
    public void ParametersSchema_ContainsQuery()
    {
        var tool = new WebSearchTool(new HttpClient());
        Assert.Contains("query", tool.ParametersSchema);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulResponse_ReturnsAbstract()
    {
        var handler = new FakeHandler("""{"AbstractText":"DuckDuckGo result","Answer":""}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.duckduckgo.com") };
        var tool = new WebSearchTool(client);

        var result = await tool.ExecuteAsync("""{"query":"test"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("DuckDuckGo result", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_AnswerFallback_ReturnsAnswer()
    {
        var handler = new FakeHandler("""{"AbstractText":"","Answer":"42"}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.duckduckgo.com") };
        var tool = new WebSearchTool(client);

        var result = await tool.ExecuteAsync("""{"query":"life"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("42", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_NoResult_ReturnsNoAnswer()
    {
        var handler = new FakeHandler("""{"AbstractText":"","Answer":""}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.duckduckgo.com") };
        var tool = new WebSearchTool(client);

        var result = await tool.ExecuteAsync("""{"query":"obscure"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("No instant answer", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_PlainTextInput_ExtractsQuery()
    {
        var handler = new FakeHandler("""{"AbstractText":"Result","Answer":""}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.duckduckgo.com") };
        var tool = new WebSearchTool(client);

        var result = await tool.ExecuteAsync("plain text query", CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_HttpFails_ReturnsError()
    {
        var handler = new FakeHandler(statusCode: HttpStatusCode.InternalServerError);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.duckduckgo.com") };
        var tool = new WebSearchTool(client);

        var result = await tool.ExecuteAsync("""{"query":"test"}""", CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly string? _content;
        private readonly HttpStatusCode _statusCode;

        public FakeHandler(string? content = null, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _content = content;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(_statusCode);
            if (_content is not null)
                response.Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        }
    }
}
