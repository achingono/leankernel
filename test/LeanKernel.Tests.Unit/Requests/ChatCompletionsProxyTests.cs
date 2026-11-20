using System.Net;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using LeanKernel;
using LeanKernel.Services.Gateway.Requests;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost:8080");
        context.Connection.LocalPort = 8080;
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
    public async Task HandleChatCompletionsRequestAsync_HostileHost_CannotChangeDestinationOrForwardExtraCredentials()
    {
        var factory = new FakeHttpClientFactory(new StubHttpMessageHandler(request =>
        {
            request.RequestUri!.Host.Should().Be("localhost");
            request.RequestUri.Port.Should().Be(8080);
            request.RequestUri.Scheme.Should().Be("http");
            request.Headers.Host.Should().Be("hostile.example:1234");
            request.Headers.Authorization!.ToString().Should().Be("Bearer test-token");
            request.Headers.GetValues("Cookie").Should().ContainSingle().Which.Should().Be(".AspNetCore.Session=session-value");
            request.Headers.Contains("X-Other-Secret").Should().BeFalse();
            request.Headers.Contains("X-Forwarded-Host").Should().BeFalse();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }));
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("hostile.example:1234");
        context.Request.Scheme = "https";
        context.Connection.LocalPort = 8080;
        context.Request.Headers.Authorization = "Bearer test-token";
        context.Request.Headers.Cookie = ".AspNetCore.Session=session-value; unrelated=secret";
        context.Request.Headers["X-Other-Secret"] = "secret";
        context.Request.Headers["X-Forwarded-Host"] = "evil.example";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"messages\":[]}"));

        var result = await IEndpointRouteBuilderExtensions.HandleChatCompletionsRequestAsync(
            "/v1/internal/completions", context, factory, Mock.Of<ILogger<HttpContext>>());

        using var executed = await ExecuteResultAsync(result);
        executed.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public void ProxyClient_DisablesRedirectsPooledCookiesAndSystemProxy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolRegistry();
        using var provider = services.BuildServiceProvider();
        var builder = provider.GetRequiredService<HttpMessageHandlerBuilder>();
        builder.Name = Constants.HttpClientNames.ChatCompletionsProxy;
        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(builder.Name);
        foreach (var configure in options.HttpMessageHandlerBuilderActions)
        {
            configure(builder);
        }

        using var handler = builder.PrimaryHandler.Should().BeOfType<SocketsHttpHandler>().Which;
        handler.AllowAutoRedirect.Should().BeFalse();
        handler.UseCookies.Should().BeFalse();
        handler.UseProxy.Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(TaskCanceledException), StatusCodes.Status504GatewayTimeout, "timed out")]
    [InlineData(typeof(HttpRequestException), StatusCodes.Status502BadGateway, "could not be reached")]
    [InlineData(typeof(InvalidOperationException), StatusCodes.Status500InternalServerError, "unexpected error")]
    public async Task HandleChatCompletionsRequestAsync_UpstreamException_ReturnsHelpfulOpenAiError(
        Type exceptionType,
        int expectedStatusCode,
        string expectedMessageFragment)
    {
        var thrownException = (Exception)Activator.CreateInstance(exceptionType, "upstream failure")!;
        var httpFactory = new FakeHttpClientFactory(new StubHttpMessageHandler(_ =>
            throw thrownException));

        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost:8080");
        context.Connection.LocalPort = 8080;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"model":"test-model","messages":[{"content":"hello","role":"user"}]}"""));

        var result = await IEndpointRouteBuilderExtensions.HandleChatCompletionsRequestAsync(
            "/v1/internal/completions",
            context,
            httpFactory,
            Mock.Of<ILogger<HttpContext>>());

        using var executed = await ExecuteResultAsync(result);
        executed.Response.StatusCode.Should().Be(expectedStatusCode);

        using var document = JsonDocument.Parse(executed.Body);
        var error = document.RootElement.GetProperty("error");
        error.GetProperty("message").GetString()!.ToLowerInvariant().Should().Contain(expectedMessageFragment);
        error.GetProperty("type").GetString().Should().Be("server_error");
        error.GetProperty("code").GetString().Should().Be("server_error");
        error.GetProperty("param").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task HandleChatCompletionsRequestAsync_ClientAborted_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost:8080");
        context.Connection.LocalPort = 8080;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"model":"test-model","messages":[{"content":"hello","role":"user"}]}"""));
        context.RequestAborted = cts.Token;

        var act = () => IEndpointRouteBuilderExtensions.HandleChatCompletionsRequestAsync(
            "/v1/internal/completions",
            context,
            new FakeHttpClientFactory(new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage()))),
            Mock.Of<ILogger<HttpContext>>());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("[{}]", "tools[0].type")]
    [InlineData("[{\"type\":null}]", "tools[0].type")]
    [InlineData("[{\"type\":7}]", "tools[0].type")]
    [InlineData("[{\"type\":\"unsupported\"}]", "tools[0].type")]
    [InlineData("[null]", "tools[0]")]
    [InlineData("{}", "tools")]
    public async Task HandleChatCompletionsRequestAsync_InvalidTools_ReturnsOpenAiBadRequest(string tools, string parameter)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"messages\":[],\"tools\":" + tools + "}"));
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);

        var result = await IEndpointRouteBuilderExtensions.HandleChatCompletionsRequestAsync(
            "/internal", context, factory.Object, Mock.Of<ILogger<HttpContext>>());

        using var executed = await ExecuteResultAsync(result);
        executed.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        using var document = JsonDocument.Parse(executed.Body);
        var error = document.RootElement.GetProperty("error");
        error.GetProperty("type").GetString().Should().Be("invalid_request_error");
        error.GetProperty("param").GetString().Should().Be(parameter);
        factory.VerifyNoOtherCalls();
    }

    [Fact]
    public void ReconstructMessage_PreservesToolPropertiesAndOrdering()
    {
        const string json = """{"messages":[],"tools":[{"function":{"name":"first","parameters":{"type":"object"}},"extra":null,"type":"function"},{"type":"function","function":{"name":"second"}}],"tool_choice":"none"}""";
        using var doc = JsonDocument.Parse(IEndpointRouteBuilderExtensions.ReconstructMessage(json, out var invalidTool));
        invalidTool.Should().BeNull();
        var tools = doc.RootElement.GetProperty("tools");
        tools[0].EnumerateObject().Last().Name.Should().Be("type");
        tools[0].GetProperty("function").GetProperty("parameters").GetProperty("type").GetString().Should().Be("object");
        tools[0].GetProperty("extra").ValueKind.Should().Be(JsonValueKind.Null);
        tools[1].GetProperty("function").GetProperty("name").GetString().Should().Be("second");
        doc.RootElement.GetProperty("tool_choice").GetString().Should().Be("none");
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
