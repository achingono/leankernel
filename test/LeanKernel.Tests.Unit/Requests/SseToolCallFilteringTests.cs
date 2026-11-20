using System.IO.Pipelines;
using System.Net;
using System.Text;

using FluentAssertions;

using LeanKernel.Services.Gateway.Requests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Requests;

/// <summary>Exercises the chat proxy's streaming transport and event handling.</summary>
public class SseToolCallFilteringTests
{
    [Fact]
    public async Task Proxy_ForwardsFirstEventBeforeUpstreamCompletes()
    {
        var pipe = new Pipe();
        var content = new StreamContent(pipe.Reader.AsStream());
        content.Headers.ContentType = new("text/event-stream");
        using var handler = new ResponseHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        var context = CreateContext(new SignalingStream());
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Connection.LocalPort = 8080;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}"));

        var result = await IEndpointRouteBuilderExtensions.HandleChatCompletionsRequestAsync(
            "/v1/internal/completions", context, new Factory(handler), Mock.Of<ILogger<HttpContext>>()).WaitAsync(TimeSpan.FromSeconds(3));
        var execution = result.ExecuteAsync(context);
        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"hi\"},\"finish_reason\":null}]}\n\n"));
        await ((SignalingStream)context.Response.Body).Written.Task.WaitAsync(TimeSpan.FromSeconds(3));
        execution.IsCompleted.Should().BeFalse();
        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n"));
        await pipe.Writer.CompleteAsync();
        await execution.WaitAsync(TimeSpan.FromSeconds(3));
        ReadBody(context).Should().EndWith("data: [DONE]\n\n");
    }

    [Fact]
    public async Task Events_FilterToolCallsStructurallyAndPreserveMixedTextAndMetadata()
    {
        var input = "data:{\"choices\":[{\"index\":0,\"delta\":{\"content\":\"literal tool_calls\",\"tool_calls\":[{\"id\":\"secret\"}]},\"finish_reason\":null}]}\r\n\r\n"
            + "data: {\"choices\":[\r\ndata: {\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\r\n\r\n"
            + "data: {\"choices\":[],\"usage\":{\"total_tokens\":4}}\r\n\r\n"
            + "data: [DONE]\r\n\r\ndata: [DONE]\r\n\r\n";
        var (context, _) = await ExecuteAsync(input, "text/event-stream");
        var body = ReadBody(context);
        body.Should().Contain("literal tool_calls").And.Contain("total_tokens").And.NotContain("secret");
        body.Split("[DONE]").Length.Should().Be(2);
    }

    [Fact]
    public async Task ToolHandoffs_AreHiddenWhileFinalTextAndCompletionRemain()
    {
        const string input = """
            data: {"choices":[{"index":0,"delta":{"content":"before","tool_calls":[{"id":"secret"}],"function_call":{"name":"legacy"}},"finish_reason":"tool_calls"}]}

            data: {"choices":[{"index":0,"delta":{"content":"after"},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            """;
        var (context, _) = await ExecuteAsync(input + "\n", "text/event-stream");
        var body = ReadBody(context);

        body.Should().Contain("before").And.Contain("after").And.Contain("[DONE]");
        body.Should().NotContain("secret").And.NotContain("legacy").And.NotContain("tool_calls").And.NotContain("function_call");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task FinishedChoiceFollowedByUnfinishedChoice_DoesNotInventCompletion(int nextIndex)
    {
        var input = "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n"
            + $"data: {{\"choices\":[{{\"index\":{nextIndex},\"delta\":{{\"content\":\"unfinished\"}},\"finish_reason\":null}}]}}\n\n";
        var (context, _) = await ExecuteAsync(input, "text/event-stream");

        ReadBody(context).Should().Contain("unfinished").And.NotContain("[DONE]");
    }

    [Fact]
    public async Task UnterminatedToolEvent_IsNotExposed()
    {
        const string input = "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"id\":\"secret\"}]}}]}";
        var (context, _) = await ExecuteAsync(input, "text/event-stream");

        ReadBody(context).Should().NotContain("secret").And.NotContain("[DONE]");
    }

    [Fact]
    public async Task MalformedChoicesWithUsage_DoNotInventCompletion()
    {
        const string input = "data: {\"choices\":[{\"index\":0,\"finish_reason\":\"stop\"}]}\n\n"
            + "data: {\"choices\":{},\"usage\":{}}\n\n";
        var (context, _) = await ExecuteAsync(input, "text/event-stream");

        ReadBody(context).Should().NotContain("[DONE]");
    }

    [Fact]
    public async Task EventsAfterDone_AreNotForwarded()
    {
        const string input = "data: [DONE]\n\ndata: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"late\"}}]}\n\n";
        var (context, _) = await ExecuteAsync(input, "text/event-stream");

        ReadBody(context).Should().Be("data: [DONE]\n\n");
    }

    [Fact]
    public async Task UpstreamAfterDone_IsDrainedBeforeDisposal()
    {
        var pipe = new Pipe();
        var content = new StreamContent(pipe.Reader.AsStream());
        content.Headers.ContentType = new("text/event-stream");
        var context = CreateContext(new SignalingStream());
        var result = new SseToolCallFilteringResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }, new CancellationTokenSource(), "/internal", Mock.Of<ILogger<HttpContext>>());
        var execution = result.ExecuteAsync(context);

        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("data: [DONE]\n\n"));
        await ((SignalingStream)context.Response.Body).Written.Task.WaitAsync(TimeSpan.FromSeconds(3));
        execution.IsCompleted.Should().BeFalse();
        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("data: discarded\n\n"));
        await pipe.Writer.CompleteAsync();
        await execution.WaitAsync(TimeSpan.FromSeconds(3));

        ReadBody(context).Should().Be("data: [DONE]\n\n");
    }

    [Theory]
    [InlineData("data: {\"error\":{\"message\":\"failed\"}}\n\ndata: {\"choices\":[{\"index\":0,\"finish_reason\":\"stop\"}]}\n\n")]
    [InlineData("data: {\"choices\":[{\"index\":0,\"finish_reason\":\"stop\"}]}\n\ndata: {\"partial\"")]
    [InlineData("data: {\"choices\":[{\"index\":0,\"finish_reason\":\"tool_calls\"}]}\n\n")]
    public async Task IncompleteOrFailedEvents_DoNotInventCompletion(string input)
    {
        var (context, _) = await ExecuteAsync(input, "text/event-stream");
        ReadBody(context).Should().NotContain("[DONE]");
    }

    [Fact]
    public async Task NonStreamingJson_IsCopiedWithoutRewriting()
    {
        const string json = "{\"content\":\"tool_calls\",\"choices\":[]}";
        var (context, _) = await ExecuteAsync(json, "application/json");
        ReadBody(context).Should().Be(json);
    }

    [Fact]
    public async Task UpstreamError_PreservesStatusAndBody()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"error\":\"busy\"}", Encoding.UTF8, "application/json"),
        };
        var context = CreateContext(new MemoryStream());
        var result = new SseToolCallFilteringResult(response, new CancellationTokenSource(), "/internal", Mock.Of<ILogger<HttpContext>>());

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        ReadBody(context).Should().Be("{\"error\":\"busy\"}");
    }

    [Fact]
    public async Task ClientAbort_CancelsStalledBodyAndDisposesUpstream()
    {
        var pipe = new Pipe();
        var stream = new TrackedStream(pipe.Reader.AsStream());
        var content = new StreamContent(stream);
        content.Headers.ContentType = new("text/event-stream");
        using var abort = new CancellationTokenSource();
        var context = CreateContext(new MemoryStream());
        context.RequestAborted = abort.Token;
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(abort.Token);
        var result = new SseToolCallFilteringResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }, timeout, "/internal", Mock.Of<ILogger<HttpContext>>());
        var execution = result.ExecuteAsync(context);

        abort.Cancel();

        await execution.Invoking(task => task.WaitAsync(TimeSpan.FromSeconds(3))).Should().ThrowAsync<OperationCanceledException>();
        stream.Disposed.Should().BeTrue();
        ReadBody(context).Should().NotContain("[DONE]");
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task StalledBodyTimeout_DisposesUpstreamAndDoesNotInventCompletion()
    {
        var pipe = new Pipe();
        var stream = new TrackedStream(pipe.Reader.AsStream());
        var content = new StreamContent(stream);
        content.Headers.ContentType = new("text/event-stream");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var result = new SseToolCallFilteringResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }, timeout, "/internal", Mock.Of<ILogger<HttpContext>>());
        var context = CreateContext(new MemoryStream());
        await result.ExecuteAsync(context).WaitAsync(TimeSpan.FromSeconds(3));
        context.Response.StatusCode.Should().Be(StatusCodes.Status504GatewayTimeout);
        stream.Disposed.Should().BeTrue();
        ReadBody(context).Should().NotContain("[DONE]");
        await pipe.Writer.CompleteAsync();
    }

    private static async Task<(DefaultHttpContext Context, HttpResponseMessage Response)> ExecuteAsync(string input, string mediaType)
    {
        var content = new StringContent(input, Encoding.UTF8, mediaType);
        var upstream = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        var context = CreateContext(new MemoryStream());
        var result = new SseToolCallFilteringResult(upstream, new CancellationTokenSource(), "/internal", Mock.Of<ILogger<HttpContext>>());
        await result.ExecuteAsync(context);
        return (context, upstream);
    }

    private static DefaultHttpContext CreateContext(Stream output)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.Response.Body = output;
        return context;
    }

    private static string ReadBody(HttpContext context) => Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class SignalingStream : MemoryStream
    {
        public TaskCompletionSource Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer, cancellationToken);
            Written.TrySetResult();
        }
    }

    private sealed class TrackedStream(Stream inner) : Stream
    {
        public bool Disposed { get; private set; }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
