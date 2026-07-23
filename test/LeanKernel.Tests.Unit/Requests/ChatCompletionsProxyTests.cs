using System.Net;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using LeanKernel.Gateway.Requests;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Requests;

/// <summary>
/// Unit tests for the chat-completions proxy helper methods.
/// </summary>
public class ChatCompletionsProxyTests
{
    [Fact]
    public async Task HandleChatCompletionsRequestAsync_EmptyPayload_ReturnsBadRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream();

        var result = await IEndpointRouteBuilderExtensions.HandleChatCompletionsRequestAsync(
            "/v1/internal/completions",
            context,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<HttpContext>>());

        result.Should().BeAssignableTo<IStatusCodeHttpResult>();
        result.As<IStatusCodeHttpResult>().StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task HandleChatCompletionsRequestAsync_ValidPayload_ForwardsAndReturnsStreamedResponse()
    {
        var responseJson = "{" + "\"choices\":[{\"message\":{\"content\":\"ok\"}}]}";
        var httpFactory = new FakeHttpClientFactory(new StubHttpMessageHandler(async request =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/v1/internal/completions");
            var forwardedBody = await request.Content!.ReadAsStringAsync();
            using var forwardedDoc = JsonDocument.Parse(forwardedBody);
            var forwardedMessage = forwardedDoc.RootElement.GetProperty("messages")[0];
            forwardedMessage.EnumerateObject().First().Name.Should().Be("role");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }));

        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"model":"test-model","messages":[{"content":"hello","role":"user"}]}"""));

        var result = await IEndpointRouteBuilderExtensions.HandleChatCompletionsRequestAsync(
            "/v1/internal/completions",
            context,
            httpFactory,
            Mock.Of<ILogger<HttpContext>>());

        using var executed = await ExecuteResultAsync(result);
        executed.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        executed.Body.Should().Contain("\"choices\"");
    }

    [Fact]
    public void MapProxiedOpenAIChatCompletions_RegistersPublicAndInternalRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        var app = builder.Build();

        app.MapProxiedOpenAIChatCompletions("leankernel", "/v1/internal/completions");

        var routeBuilder = (IEndpointRouteBuilder)app;
        var routePatterns = routeBuilder.DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();

        routePatterns.Should().Contain("/v1/chat/completions");
        routePatterns.Should().Contain("/v1/internal/completions/");
    }

    [Fact]
    public async Task MapOpenAIModels_ReturnsSingleConfiguredModel()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        app.MapOpenAIModels("leankernel");

        var routeBuilder = (IEndpointRouteBuilder)app;
        var routePatterns = routeBuilder.DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();

        routePatterns.Should().Contain("/v1/models");

        var endpoint = routeBuilder.DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .First(routeEndpoint => routeEndpoint.RoutePattern.RawText == "/v1/models");

        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("object").GetString().Should().Be("list");
        var data = document.RootElement.GetProperty("data");
        data.GetArrayLength().Should().Be(1);
        data[0].GetProperty("id").GetString().Should().Be("leankernel");
        data[0].GetProperty("object").GetString().Should().Be("model");
    }

    private static async Task<ExecutedResult> ExecuteResultAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        return new ExecutedResult(context.Response, body);
    }

    private sealed record ExecutedResult(HttpResponse Response, string Body) : IDisposable
    {
        public void Dispose()
        {
            Response.Body.Dispose();
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => sendAsync(request);
    }
}
