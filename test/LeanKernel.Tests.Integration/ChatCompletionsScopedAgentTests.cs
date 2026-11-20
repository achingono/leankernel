using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace LeanKernel.Tests.Integration;

/// <summary>
/// Verifies chat-completions endpoint behavior when keyed agents are resolved per request scope.
/// </summary>
public class ChatCompletionsScopedAgentTests : IClassFixture<ChatCompletionsScopedAgentTests.ScopedAgentGatewayTestApplicationFactory>
{
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a test instance.
    /// </summary>
    /// <param name="factory">The configured test host factory.</param>
    public ChatCompletionsScopedAgentTests(ScopedAgentGatewayTestApplicationFactory factory)
    {
        ScopedAgentRequestTracker.Reset();
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Confirms two independent chat-completions requests use different scoped agent instances
    /// and still return the expected OpenAI completion response shape.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task PostChatCompletions_SeparateRequests_UseDistinctScopedAgentsAndReturnCompletionShape()
    {
        var firstResponse = await SendChatCompletionAsync("request-1", "First prompt");
        var secondResponse = await SendChatCompletionAsync("request-2", "Second prompt");

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await AssertCompletionShapeAsync(firstResponse);
        await AssertCompletionShapeAsync(secondResponse);

        ScopedAgentRequestTracker.TryGetAgentInstance("request-1", out var firstAgentId).Should().BeTrue();
        ScopedAgentRequestTracker.TryGetAgentInstance("request-2", out var secondAgentId).Should().BeTrue();
        firstAgentId.Should().NotBe(secondAgentId);
    }

    /// <summary>Verifies client tool definitions reach the real MAF JSON deserializer regardless of property order.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostChatCompletions_ToolDiscriminatorAfterFunction_IsAccepted(bool stream)
    {
        var json = """{"model":"test-model","messages":[{"role":"user","content":"test"}],"tools":[{"function":{"name":"first","parameters":{"type":"object","properties":{}}},"type":"function"},{"type":"function","function":{"name":"second","parameters":{"type":"object","properties":{}}}}],"tool_choice":"none","stream":STREAM}""".Replace("STREAM", stream ? "true" : "false", StringComparison.Ordinal);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Accepts out-of-order polymorphic metadata in nested message content and tool choice.</summary>
    [Fact]
    public async Task PostChatCompletions_NestedDiscriminatorsAfterData_AreAccepted()
    {
        const string json = """{"model":"test-model","messages":[{"role":"user","content":[{"text":"test","type":"text"}]}],"tools":[{"type":"function","function":{"name":"first","parameters":{"type":"object","properties":{}}}}],"tool_choice":{"function":{"name":"first"},"type":"function"}}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Accepts out-of-order input content discriminators on the responses endpoint.</summary>
    [Fact]
    public async Task PostResponses_InputDiscriminatorsAfterData_AreAccepted()
    {
        const string json = """{"model":"test-model","input":[{"role":"user","content":[{"text":"test","type":"input_text"}],"type":"message"}],"agent":{"name":"leankernel"}}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> SendChatCompletionAsync(string requestId, string prompt)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "test-model",
                messages = new[]
                {
                    new
                    {
                        content = prompt,
                        role = "user",
                    },
                },
            }),
        };

        request.Headers.Add("X-Correlation-ID", requestId);

        return await _client.SendAsync(request);
    }

    private static async Task AssertCompletionShapeAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        root.TryGetProperty("choices", out var choices).Should().BeTrue();
        choices.ValueKind.Should().Be(JsonValueKind.Array);
        choices.GetArrayLength().Should().BeGreaterThan(0);
        choices[0].TryGetProperty("message", out var message).Should().BeTrue();
        message.TryGetProperty("content", out var content).Should().BeTrue();
        content.GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Configures the gateway factory with a tracked scoped keyed agent for chat completions.
    /// </summary>
    public sealed class ScopedAgentGatewayTestApplicationFactory : GatewayTestApplicationFactory
    {
        /// <inheritdoc />
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddTransient<IStartupFilter, TestServerLocalPortFilter>();
                var keyedAgentDescriptors = services
                    .Where(descriptor => descriptor.ServiceType == typeof(AIAgent)
                                         && descriptor.IsKeyedService
                                         && descriptor.ServiceKey is string key
                                         && key == "leankernel")
                    .ToList();

                foreach (var descriptor in keyedAgentDescriptors)
                {
                    services.Remove(descriptor);
                }

                var httpClientFactoryDescriptors = services
                    .Where(descriptor => descriptor.ServiceType == typeof(IHttpClientFactory))
                    .ToList();

                foreach (var descriptor in httpClientFactoryDescriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddKeyedScoped<AIAgent>("leankernel", static (sp, _) =>
                    new TrackingAIAgent(sp.GetRequiredService<IHttpContextAccessor>()));

                services.AddSingleton<IHttpClientFactory>(_ =>
                    new LoopbackHttpClientFactory(() => Server.CreateHandler()));
            });
        }
    }

    private sealed class TestServerLocalPortFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                context.Connection.LocalPort = 80;
                await continuation();
            });
            next(app);
        };
    }

    private sealed class TrackingAIAgent(IHttpContextAccessor httpContextAccessor) : AIAgent
    {
        private readonly Guid _instanceId = Guid.NewGuid();

        public override string? Name => "leankernel";

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<AgentSession>(new TrackingAgentSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(JsonDocument.Parse("{}").RootElement);

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<AgentSession>(new TrackingAgentSession());

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CaptureRequest();
            var response = new AgentResponse(new ChatMessage(ChatRole.Assistant, $"ok:{_instanceId:N}"));
            return Task.FromResult(response);
        }

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CaptureRequest();
            yield return new AgentResponseUpdate(new ChatResponseUpdate { Role = ChatRole.Assistant });
            await Task.CompletedTask;
        }

        private void CaptureRequest()
        {
            var requestId = httpContextAccessor.HttpContext?.Request.Headers["X-Correlation-ID"].ToString();
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                ScopedAgentRequestTracker.Record(requestId, _instanceId);
            }
        }
    }

    private sealed class TrackingAgentSession : AgentSession
    {
    }

    private sealed class LoopbackHttpClientFactory(Func<HttpMessageHandler> handlerFactory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var rewriteHandler = new LoopbackRewriteHandler
            {
                InnerHandler = handlerFactory(),
            };

            return new HttpClient(rewriteHandler, disposeHandler: true);
        }
    }

    private sealed class LoopbackRewriteHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is { } uri)
            {
                var builder = new UriBuilder(uri)
                {
                    Scheme = "http",
                    Host = "localhost",
                    Port = 80,
                };

                request.RequestUri = builder.Uri;
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    private static class ScopedAgentRequestTracker
    {
        private static readonly ConcurrentDictionary<string, Guid> RequestAgentMap = new(StringComparer.Ordinal);

        public static void Reset()
        {
            RequestAgentMap.Clear();
        }

        public static void Record(string requestId, Guid agentInstanceId)
        {
            RequestAgentMap[requestId] = agentInstanceId;
        }

        public static bool TryGetAgentInstance(string requestId, out Guid agentInstanceId)
            => RequestAgentMap.TryGetValue(requestId, out agentInstanceId);
    }
}
